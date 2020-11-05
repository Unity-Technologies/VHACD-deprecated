package com.unity.nats

import com.fasterxml.jackson.annotation.JsonIgnoreProperties
import com.fasterxml.jackson.annotation.JsonProperty
import com.fasterxml.jackson.databind.JsonNode
import com.fasterxml.jackson.databind.ObjectMapper
import com.fasterxml.jackson.databind.node.ObjectNode
import com.fasterxml.jackson.module.kotlin.readValue
import com.fasterxml.jackson.module.kotlin.registerKotlinModule
import com.unity.template.monitoring.MonitoringMetric
import com.unity.template.monitoring.initCounter
import com.unity.template.monitoring.initHistogram
import com.unity.template.utils.getRootCause
import io.micrometer.prometheus.PrometheusMeterRegistry
import io.nats.client.Connection
import io.nats.client.Message
import io.prometheus.client.Counter
import io.prometheus.client.Histogram
import io.prometheus.client.Histogram.Timer
import mu.KLogging
import org.slf4j.MDC

// These have to be public in order for inline method to be able to use these
val OBJECT_MAPPER = ObjectMapper().registerKotlinModule()
const val QUEUE_NAME = "todo_change_me"

class NatsJsonMessageHandler(val connection: Connection, meterRegistry: PrometheusMeterRegistry) : KLogging() {

    val monitor = NatsMonitor(meterRegistry)

    /**
     * Reified generics only works for inlineable functions. If we don't inline this function
     * and therefore cannot use the reified annotation on the type T, the OBJECT_MAPPER.readValue<T>
     * doesn't know what the type T is.
     */
    inline fun <reified T> registerHandler(subscription: AdsNatsSubscription<T>) {
        val dispatcher = connection.createDispatcher { message ->

            checkNotNull(message)

            when (subscription) {
                is AdsNatsSubscriptionWithAck -> handleAck(message, subscription)
                is AdsNatsSubscriptionWithReply -> handleReply(message, subscription)
                else -> throw UnknownSubscriptionTypeException(subscription.javaClass.name)
            }
        }

        dispatcher.subscribe(subscription.subject, QUEUE_NAME)
        if (subscription is AdsNatsSubscriptionWithAck) {
            dispatcher.subscribe(subscription.retrySubject, QUEUE_NAME)
        }
    }

    /**
     * Sends ACK, if message handling is successful.
     * Otherwise doesn't return anything, but logs error.
     */
    inline fun <reified T> handleAck(message: Message, subscription: AdsNatsSubscriptionWithAck<T>) {
        val requestTimer: Timer = monitor.startTimer(message.subject)
        try {
            val incomingAckMessage: IncomingAckMessage<T> = parseIncomingAckMessage(message)

            MDC.put("correlationId", incomingAckMessage.correlationId())
            logger.info { "Message received from subject: ${message.subject}" }

            subscription.handleJsonMessage(incomingAckMessage.data)

            // this is only success message
            sendReplyMessage(subscription.ackSubject, incomingAckMessage.acknowledgement)
            logAndMonitorSuccess(message)
        } catch (e: NatsAwareException) {
            // Ack messages do not reply with error, so just log it
            logAndMonitorError(message, e, e.natsErrorType)
        } catch (e: Exception) {
            // Ack messages do not reply with error, so just log it
            logAndMonitorError(message, e, NatsErrorType.UNEXPECTED_ERROR)
        } finally {
            monitor.finishedMessage(requestTimer)
            MDC.clear()
        }
    }

    /**
     * Sends always REPLY message, either success with expected response or error message with reason.
     */
    inline fun <reified T> handleReply(message: Message, subscription: AdsNatsSubscriptionWithReply<T>) {
        val requestTimer: Timer = monitor.startTimer(message.subject)
        try {
            // this can be either success or error message
            sendReplyMessage(
                message.replyTo,
                getReplyMessage(message, subscription)
            )
        } catch (e: Exception) {
            // at this point the message sending failed and no reply can be sent
            logAndMonitorError(message, e, NatsErrorType.UNEXPECTED_ERROR)
        } finally {
            monitor.finishedMessage(requestTimer)
            MDC.clear()
        }
    }

    inline fun <reified T> getReplyMessage(message: Message, subscription: AdsNatsSubscriptionWithReply<T>): JsonNode {
        return try {
            val incomingMessage: IncomingReplyMessage<T> = parseIncomingReplyMessage(message)

            MDC.put("correlationId", incomingMessage.correlationId())
            logger.info { "Message received from subject: ${message.subject}" }

            val result = subscription.handleJsonMessage(incomingMessage.data)
            logAndMonitorSuccess(message)
            return result
        } catch (e: InvalidMessageException) {
            logAndMonitorError(message, e, NatsErrorType.BAD_REQUEST_ERROR)
            createErrorMessage(NatsErrorType.BAD_REQUEST_ERROR, e)
        } catch (e: NatsAwareException) {
            logAndMonitorError(message, e, e.natsErrorType)
            createErrorMessage(e.natsErrorType, e)
        } catch (e: Exception) {
            logAndMonitorError(message, e, NatsErrorType.UNEXPECTED_ERROR)
            createErrorMessage(exception = e)
        }
    }

    inline fun <reified T> parseIncomingAckMessage(message: Message): IncomingAckMessage<T> {
        val data = checkAndGetData(message)
        return IncomingAckMessage<T>(
            acknowledgement = readRequiredMessageFields(data),
            data = readMessageData(data)
        )
    }

    inline fun <reified T> parseIncomingReplyMessage(message: Message): IncomingReplyMessage<T> {
        val data = checkAndGetData(message)
        return IncomingReplyMessage<T>(
            metadata = readRequiredMessageFields(data),
            data = readMessageData(data)
        )
    }

    inline fun <reified T> readMessageData(data: ByteArray): T {
        try {
            return OBJECT_MAPPER.readValue<T>(data)
        } catch (e: com.fasterxml.jackson.module.kotlin.MissingKotlinParameterException) {
            // custom handling here, as the default error message exposes too much internal details and is very verbose
            throw InvalidMessageException("Message data is missing value for ${e.parameter.name}")
        } catch (e: java.io.IOException) {
            throw InvalidMessageException(getRootCause(e).message ?: "Message data is missing required values")
        }
    }

    inline fun <reified T> readRequiredMessageFields(message: ByteArray): T {
        try {
            return OBJECT_MAPPER.readValue<T>(message)
        } catch (e: com.fasterxml.jackson.module.kotlin.MissingKotlinParameterException) {
            // custom handling here, as the default error message exposes too much internal details and is very verbose
            throw InvalidMessageException("Message is missing value for ${e.parameter.name}")
        } catch (e: java.io.IOException) {
            throw InvalidMessageException(getRootCause(e).message ?: "Message is missing required values")
        }
    }

    fun checkAndGetData(message: Message): ByteArray {
        if (message.data == null || message.data.isEmpty()) {
            throw EmptyRequestDataException(message.subject)
        }
        return message.data
    }

    fun logAndMonitorError(message: Message, e: Exception, natsErrorType: NatsErrorType) {
        val data = message.data?.let { String(it) } ?: "No data"
        logger.error(e) { "Could not handle message from subject ${message.subject}. Message: $data" }
        monitor.error(message.subject, natsErrorType.errorName)
    }

    fun logAndMonitorSuccess(message: Message) {
        logger.info { "Message from subject ${message.subject} handled successfully." }
        monitor.success(message.subject)
    }

    fun sendReplyMessage(subject: String, reply: Any) {
        connection.publish(subject, OBJECT_MAPPER.writeValueAsBytes(reply))
    }

    fun createErrorMessage(errorType: NatsErrorType = NatsErrorType.UNEXPECTED_ERROR, exception: Exception): JsonNode {
        return OBJECT_MAPPER.valueToTree(NatsErrorReply(errorType.errorName, exception.message ?: ""))
    }
}

class NatsMonitor(meterRegistry: PrometheusMeterRegistry) : KLogging() {

    private val latencyHistogram: Histogram = initHistogram(Metric.HISTOGRAM, meterRegistry, "subject")
    private val totalRequestsCounter: Counter = initCounter(Metric.TOTAL, meterRegistry, "subject", "success", "status")

    fun startTimer(subject: String): Timer {
        return latencyHistogram
            .labels(subject)
            .startTimer()
    }

    fun finishedMessage(timer: Timer) {
        timer.observeDuration()
    }

    fun success(subject: String) {
        totalRequestsCounter.labels(subject, "true", "OK").inc()
    }

    fun error(subject: String, status: String) {
        totalRequestsCounter.labels(subject, "false", status).inc()
    }

    enum class Metric(override val metricName: String, override val description: String) : MonitoringMetric {
        TOTAL("nats_server_handled_total", "NATS server handled requests counter"),
        HISTOGRAM("nats_server_handling_seconds", "NATS server handled requests duration histogram");
    }
}

@JsonIgnoreProperties(ignoreUnknown = true)
data class Acknowledgement(
    @JsonProperty("id") val id: String,
    @JsonProperty("messageId") val messageId: String,
    @JsonProperty("correlationId") val correlationId: String
)

data class IncomingAckMessage<T>(
    val acknowledgement: Acknowledgement,
    val data: T
) {
    fun correlationId(): String {
        return acknowledgement.correlationId
    }
}

@JsonIgnoreProperties(ignoreUnknown = true)
data class IncomingReplyMessageMetadata(
    @JsonProperty("params") val params: ObjectNode,
    @JsonProperty("correlationId") val correlationId: String
)

data class IncomingReplyMessage<T>(
    val metadata: IncomingReplyMessageMetadata,
    val data: T
) {
    fun correlationId(): String {
        return metadata.correlationId
    }
}

data class NatsErrorReply(val error: Error) {
    constructor(name: String, message: String) : this(Error(name, message))

    data class Error(val name: String, val message: String)
}

class UnknownSubscriptionTypeException(className: String) :
    NatsAwareException(NatsErrorType.UNEXPECTED_ERROR, "Can not handle unknown subscription of type $className")

class InvalidMessageException(reason: String) :
    NatsAwareException(NatsErrorType.BAD_REQUEST_ERROR, "Incoming message is invalid: $reason")

class EmptyRequestDataException(subject: String) :
    NatsAwareException(NatsErrorType.BAD_REQUEST_ERROR, "Incoming subscription $subject message has no data")
