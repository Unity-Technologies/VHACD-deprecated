package com.unity.nats

import com.fasterxml.jackson.annotation.JsonIgnoreProperties
import com.fasterxml.jackson.annotation.JsonProperty
import com.fasterxml.jackson.databind.ObjectMapper
import com.fasterxml.jackson.module.kotlin.readValue
import com.fasterxml.jackson.module.kotlin.registerKotlinModule
import io.micrometer.prometheus.PrometheusMeterRegistry
import io.mockk.mockk
import io.nats.client.Connection
import io.nats.client.Nats
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Nested
import org.junit.jupiter.api.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

private const val TEST_SUBJECT = "ack-test.subject"
private const val ACK_SUBJECT = "ack.$TEST_SUBJECT"
private const val RETRY_SUBJECT = "retry.$TEST_SUBJECT"
private const val FAULTY_TEST_SUBJECT = "faulty.$TEST_SUBJECT"
private const val FAULTY_ACK_SUBJECT = "faulty.ack.$FAULTY_TEST_SUBJECT"
private const val FAULTY_RETRY_SUBJECT = "faulty.retry.$FAULTY_TEST_SUBJECT"

private const val EXCEPTION_MESSAGE = "Error thrown for testing purposes"

private val OBJECT_MAPPER = ObjectMapper().registerKotlinModule()

internal class NatsJsonAckMessageHandlerTest {

    lateinit var connection: Connection
    val meterRegistry: PrometheusMeterRegistry = mockk(relaxed = true)

    fun createNatsUrl() = "nats://${System.getenv("NATS_SERVERS") ?: "localhost:4222"}"

    fun createNatsConnection() = Nats.connect(createNatsUrl())

    @BeforeEach
    internal fun setUp() {
        connection = createNatsConnection()
    }

    @AfterEach
    internal fun tearDown() {
        // Closing of connection between tests is required
        connection.close()
    }

    @Nested
    inner class SuccessfulAckHandler {

        lateinit var handlerCountDownLatch: CountDownLatch
        lateinit var natsJsonMessageHandler: NatsJsonMessageHandler

        @BeforeEach
        internal fun setUp() {
            handlerCountDownLatch = CountDownLatch(1)

            natsJsonMessageHandler = NatsJsonMessageHandler(connection, meterRegistry)
            natsJsonMessageHandler.registerHandler(AckTestHandler(handlerCountDownLatch))
        }

        @Test
        fun `json message handler takes in messages and parses them to json`() {
            val testMessage = createTestJson()

            assertJsonMessageHandlerReadsMessagesFromSubject(testMessage, TEST_SUBJECT, handlerCountDownLatch)
        }

        @Test
        fun `json acknowledgement message is created from the input message`() {
            val ackCountDownLatch = createAckSubscriptionAndCountDownLatch()
            val testMessage = createTestJson()

            connection.publish(TEST_SUBJECT, testMessage.toByteArray())

            assertTrue(ackCountDownLatch.await(10, TimeUnit.SECONDS))
        }

        @Test
        fun `json handler with acknowledgement support fails when correlation id is missing`() {
            val ackCountDownLatch = createAckSubscriptionAndCountDownLatch()
            val testMessage =
                """
                |{
                |  "id": "entity-id",
                |  "messageId": "message-id",
                |  "data": {"testProperty": "test"},
                |  "shouldBeIgnored": "ignore-this-stuff-and-dont-fail"
                |}""".trimMargin()

            assertAcknowledgementParsingFails(testMessage, handlerCountDownLatch)
            assertFalse(ackCountDownLatch.await(500, TimeUnit.MILLISECONDS))
        }

        @Test
        fun `json handler with acknowledgement support fails when id is missing`() {
            val ackCountDownLatch = createAckSubscriptionAndCountDownLatch()
            val testMessage =
                """
                |{
                |  "correlationId": "correlation-id-test",
                |  "messageId": "message-id",
                |  "data": {"testProperty": "test"},
                |  "shouldBeIgnored": "ignore-this-stuff-and-dont-fail"
                |}""".trimMargin()

            assertAcknowledgementParsingFails(testMessage, handlerCountDownLatch)
            assertFalse(ackCountDownLatch.await(500, TimeUnit.MILLISECONDS))
        }

        @Test
        fun `json handler with acknowledgement support fails when message id is missing`() {
            val ackCountDownLatch = createAckSubscriptionAndCountDownLatch()
            val testMessage =
                """
                |{
                |  "correlationId": "correlation-id-test",
                |  "id": "entity-id",
                |  "data": {"testProperty": "test"},
                |  "shouldBeIgnored": "ignore-this-stuff-and-dont-fail"
                |}""".trimMargin()

            assertAcknowledgementParsingFails(testMessage, handlerCountDownLatch)
            assertFalse(ackCountDownLatch.await(500, TimeUnit.MILLISECONDS))
        }

        @Test
        fun `json handler with acknowledgement support fails when whole message is empty`() {
            val ackCountDownLatch = createAckSubscriptionAndCountDownLatch()

            connection.publish(TEST_SUBJECT, ByteArray(0))

            assertFalse(ackCountDownLatch.await(500, TimeUnit.MILLISECONDS))
        }

        @Test
        fun `json handler also listens to retry messages and acks those`() {
            val ackCountDownLatch = createAckSubscriptionAndCountDownLatch(RETRY_SUBJECT)
            val testMessage = createTestJson()

            assertJsonMessageHandlerReadsMessagesFromSubject(testMessage, RETRY_SUBJECT, handlerCountDownLatch)
            assertTrue(ackCountDownLatch.await(10, TimeUnit.SECONDS))
        }
    }

    @Nested
    inner class FaultyAckHandler {

        lateinit var ackCountDownLatch: CountDownLatch
        lateinit var natsJsonMessageHandler: NatsJsonMessageHandler
        lateinit var testMessage: String

        @BeforeEach
        internal fun setUp() {
            natsJsonMessageHandler = NatsJsonMessageHandler(connection, meterRegistry)
            natsJsonMessageHandler.registerHandler(AckFaultyTestHandler())
            ackCountDownLatch = createAckSubscriptionAndCountDownLatch(FAULTY_ACK_SUBJECT)
            testMessage = createTestJson()
        }

        @Test
        fun `no ack message is sent if the handler throws an exception`() {
            connection.publish(FAULTY_TEST_SUBJECT, testMessage.toByteArray())

            assertFalse(ackCountDownLatch.await(500, TimeUnit.MILLISECONDS))
        }

        @Test
        fun `no ack message for retry subject is sent if the handler throws an exception`() {
            connection.publish(FAULTY_RETRY_SUBJECT, testMessage.toByteArray())

            assertFalse(ackCountDownLatch.await(500, TimeUnit.MILLISECONDS))
        }
    }

    private fun createTestJson(): String {
        return """
                |{
                |  "correlationId": "correlation-id-test",
                |  "params": {},
                |  "id": "entity-id",
                |  "messageId": "message-id",
                |  "data": {"testProperty": "test"},
                |  "shouldBeIgnored": "ignore-this-stuff-and-dont-fail"
                |}""".trimMargin()
    }

    private fun createAckSubscriptionAndCountDownLatch(subject: String = TEST_SUBJECT): CountDownLatch {
        val ackCountDownLatch = CountDownLatch(1)
        createAckSubscription(subject, ackCountDownLatch)
        return ackCountDownLatch
    }

    private fun assertAcknowledgementParsingFails(testMessage: String, countDownLatch: CountDownLatch) {
        connection.publish(TEST_SUBJECT, testMessage.toByteArray())

        assertFalse(countDownLatch.await(500, TimeUnit.MILLISECONDS))
    }

    private fun createAckSubscription(subject: String, ackCountDownLatch: CountDownLatch) {
        val ackDispatcher = connection.createDispatcher { ack ->
            val data = ack?.data ?: throw RuntimeException("There is no ack message data")
            val retrievedAck = OBJECT_MAPPER.readValue<Acknowledgement>(data)
            assertEquals("correlation-id-test", retrievedAck.correlationId)
            assertEquals("message-id", retrievedAck.messageId)
            assertEquals("entity-id", retrievedAck.id)
            ackCountDownLatch.countDown()
        }
        ackDispatcher.subscribe(subject)
    }

    private fun assertJsonMessageHandlerReadsMessagesFromSubject(
        testMessage: String,
        subject: String,
        countDownLatch: CountDownLatch
    ) {
        connection.publish(subject, testMessage.toByteArray())

        assertTrue(countDownLatch.await(10, TimeUnit.SECONDS))
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    data class TestEntity(@JsonProperty val data: Data) {
        data class Data(@JsonProperty val testProperty: String)
    }

    private class AckTestHandler(val countDownLatch: CountDownLatch) : AdsNatsSubscriptionWithAck<TestEntity> {

        override val subject = TEST_SUBJECT
        override val ackSubject = ACK_SUBJECT
        override val retrySubject = RETRY_SUBJECT

        override fun handleJsonMessage(entity: TestEntity) {
            assertEquals("test", entity.data.testProperty)
            countDownLatch.countDown()
        }

        override fun subscribe() {}
    }

    private class AckFaultyTestHandler : AdsNatsSubscriptionWithAck<TestEntity> {

        override val subject = FAULTY_TEST_SUBJECT
        override val ackSubject = FAULTY_ACK_SUBJECT
        override val retrySubject = FAULTY_RETRY_SUBJECT

        override fun handleJsonMessage(entity: TestEntity) {
            throw RuntimeException(EXCEPTION_MESSAGE)
        }

        override fun subscribe() {}
    }
}
