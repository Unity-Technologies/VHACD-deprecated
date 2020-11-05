package com.unity.nats

import com.fasterxml.jackson.annotation.JsonIgnoreProperties
import com.fasterxml.jackson.annotation.JsonProperty
import com.fasterxml.jackson.databind.JsonNode
import com.fasterxml.jackson.databind.ObjectMapper
import com.fasterxml.jackson.module.kotlin.readValue
import com.fasterxml.jackson.module.kotlin.registerKotlinModule
import io.micrometer.prometheus.PrometheusMeterRegistry
import io.mockk.mockk
import io.nats.client.Connection
import io.nats.client.Nats
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Nested
import org.junit.jupiter.api.Test
import java.time.Duration

private const val TEST_SUBJECT = "reply-test.subject"
private const val FAULTY_TEST_SUBJECT = "faulty.$TEST_SUBJECT"
private const val EXCEPTION_MESSAGE = "Error thrown for testing purposes"

private val OBJECT_MAPPER = ObjectMapper().registerKotlinModule()

internal class NatsJsonReplyMessageHandlerTest {

    lateinit var connection: Connection
    val timeout: Duration = Duration.ofSeconds(10L)
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
    inner class SuccessfulReplyHandler {

        lateinit var natsJsonMessageHandler: NatsJsonMessageHandler

        @BeforeEach
        internal fun setUp() {
            natsJsonMessageHandler = NatsJsonMessageHandler(connection, meterRegistry)
            natsJsonMessageHandler.registerHandler(TestReplyHandler())
        }

        @Test
        fun `reply message is created for a nats message that expects a reply`() {
            val testMessage = createTestJson()

            val replyData = OBJECT_MAPPER.readValue<JsonNode>(
                connection.request(TEST_SUBJECT, testMessage.toByteArray(), timeout).data
            )

            assertTrue(replyData.get("result").get("reply").asBoolean())
        }

        @Test
        fun `json handler with reply support fails when correlation id is missing`() {
            val testMessage =
                """
                |{
                |  "params": {"userId":"123"},
                |  "data": {"testProperty": "test"},
                |  "shouldBeIgnored": "ignore-this-stuff-and-dont-fail"
                |}""".trimMargin()

            val replyData = OBJECT_MAPPER.readValue<NatsErrorReply>(
                connection.request(TEST_SUBJECT, testMessage.toByteArray(), timeout).data
            )

            assertEquals(NatsErrorType.BAD_REQUEST_ERROR.errorName, replyData.error.name)
            assertEquals(
                "Incoming message is invalid: Message is missing value for correlationId",
                replyData.error.message
            )
        }

        @Test
        fun `json handler with reply support fails when params is missing`() {
            val testMessage =
                """
                |{
                |  "correlationId": "123",
                |  "data": {"testProperty": "test"},
                |  "shouldBeIgnored": "ignore-this-stuff-and-dont-fail"
                |}""".trimMargin()

            val replyData = OBJECT_MAPPER.readValue<NatsErrorReply>(
                connection.request(TEST_SUBJECT, testMessage.toByteArray(), timeout).data
            )

            assertEquals(NatsErrorType.BAD_REQUEST_ERROR.errorName, replyData.error.name)
            assertEquals("Incoming message is invalid: Message is missing value for params", replyData.error.message)
        }

        @Test
        fun `json handler with reply support fails when data is required but is missing`() {
            val testMessage =
                """
                |{
                |  "params": {"userId":"123"},
                |  "correlationId": "123",
                |  "shouldBeIgnored": "ignore-this-stuff-and-dont-fail"
                |}""".trimMargin()

            val replyData = OBJECT_MAPPER.readValue<NatsErrorReply>(
                connection.request(TEST_SUBJECT, testMessage.toByteArray(), timeout).data
            )

            assertEquals(NatsErrorType.BAD_REQUEST_ERROR.errorName, replyData.error.name)
            assertEquals("Incoming message is invalid: Message data is missing value for data", replyData.error.message)
        }

        @Test
        fun `json handler with reply support fails when data is required but is internal values missing`() {
            val testMessage =
                """
                |{
                |  "params": {"userId":"123"},
                |  "correlationId": "123",
                |  "data": {"unknownProperty": "foo"},
                |  "shouldBeIgnored": "ignore-this-stuff-and-dont-fail"
                |}""".trimMargin()

            val replyData = OBJECT_MAPPER.readValue<NatsErrorReply>(
                connection.request(TEST_SUBJECT, testMessage.toByteArray(), timeout).data
            )

            assertEquals(NatsErrorType.BAD_REQUEST_ERROR.errorName, replyData.error.name)
            assertEquals(
                "Incoming message is invalid: Message data is missing value for testProperty",
                replyData.error.message
            )
        }
    }

    @Nested
    inner class FaultyReplyHandler {

        lateinit var natsJsonMessageHandler: NatsJsonMessageHandler

        @BeforeEach
        internal fun setUp() {
            natsJsonMessageHandler = NatsJsonMessageHandler(connection, meterRegistry)
            natsJsonMessageHandler.registerHandler(TestFaultyReplyHandler())
        }

        @Test
        fun `error message is returned for nats message that expects a reply in case of exception`() {
            val testMessage = createTestJson()

            val replyData = OBJECT_MAPPER.readValue<NatsErrorReply>(
                connection.request(FAULTY_TEST_SUBJECT, testMessage.toByteArray(), timeout).data
            )

            assertEquals(NatsErrorType.UNEXPECTED_ERROR.errorName, replyData.error.name)
            assertEquals(EXCEPTION_MESSAGE, replyData.error.message)
        }

        @Test
        fun `error message is returned for invalid nats message that expects a reply`() {
            val testMessage = ""

            val replyData = OBJECT_MAPPER.readValue<NatsErrorReply>(
                connection.request(FAULTY_TEST_SUBJECT, testMessage.toByteArray(), timeout).data
            )

            assertEquals(NatsErrorType.BAD_REQUEST_ERROR.errorName, replyData.error.name)
            assertEquals("Incoming subscription $FAULTY_TEST_SUBJECT message has no data", replyData.error.message)
        }
    }

    private fun createTestJson(): String {
        return """
                |{
                |  "correlationId": "correlation-id-test",
                |  "params": {},
                |  "data": {"testProperty": "test"},
                |  "shouldBeIgnored": "ignore-this-stuff-and-dont-fail"
                |}""".trimMargin()
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    data class TestEntity(@JsonProperty val data: Data) {
        data class Data(@JsonProperty val testProperty: String)
    }

    private class TestReplyHandler : AdsNatsSubscriptionWithReply<TestEntity> {

        override val subject = TEST_SUBJECT

        override fun handleJsonMessage(entity: TestEntity): JsonNode {
            assertEquals("test", entity.data.testProperty)

            val reply = OBJECT_MAPPER.createObjectNode()
            reply.put("reply", true)

            val result = OBJECT_MAPPER.createObjectNode()
            result.replace("result", reply)

            return result
        }

        override fun subscribe() {}
    }

    private class TestFaultyReplyHandler : AdsNatsSubscriptionWithReply<TestEntity> {

        override val subject = FAULTY_TEST_SUBJECT

        override fun handleJsonMessage(entity: TestEntity): JsonNode {
            throw RuntimeException(EXCEPTION_MESSAGE)
        }

        override fun subscribe() {}
    }
}
