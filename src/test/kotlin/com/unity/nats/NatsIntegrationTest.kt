package com.unity.nats

import com.fasterxml.jackson.databind.JsonNode
import com.fasterxml.jackson.databind.ObjectMapper
import com.fasterxml.jackson.module.kotlin.registerKotlinModule
import com.unity.nats.register as registerNats
import com.unity.template.IntegrationTest
import io.ktor.util.KtorExperimentalAPI
import io.nats.client.Connection
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.BeforeEach
import org.koin.test.inject
import java.nio.charset.StandardCharsets
import java.time.Duration
import java.util.concurrent.TimeUnit

private val OBJECT_MAPPER = ObjectMapper().registerKotlinModule()

@KtorExperimentalAPI
open class NatsIntegrationTest : IntegrationTest() {

    @BeforeEach
    fun setUpNats() {
        engine.application.registerNats()
    }

    @AfterEach
    fun tearDownNats() {
        val connection: Connection by inject()
        connection.close()
    }

    fun sendNatsMessageWithReply(subject: String, json: JsonNode, timeout: Duration): JsonNode {
        val connection: Connection by inject()

        val message = connection.request(subject, json.toString().toByteArray(StandardCharsets.UTF_8))
            .get(timeout.toMillis(), TimeUnit.MILLISECONDS) ?: throw RuntimeException("no reply / timeout")
        return OBJECT_MAPPER.readTree(message.data)
    }
}
