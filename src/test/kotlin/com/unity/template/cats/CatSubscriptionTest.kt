package com.unity.template.cats

import com.fasterxml.jackson.databind.JsonNode
import com.fasterxml.jackson.databind.node.JsonNodeFactory
import com.fasterxml.jackson.databind.node.ObjectNode
import com.unity.nats.NatsErrorType
import com.unity.nats.NatsIntegrationTest
import com.unity.nats.NatsJsonFixtures.createReplyNatsMessage
import io.ktor.util.KtorExperimentalAPI
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.koin.test.inject
import java.time.Duration
import com.unity.template.cats.register as registerCats

@KtorExperimentalAPI
class CatSubscriptionTest : NatsIntegrationTest() {

    val timeout = Duration.ofSeconds(10L)
    val SUBJECT = "find.ads-kotlin-service-template.cats"
    lateinit var addedCat: Kitty

    @BeforeEach
    fun setUp() {
        engine.application.registerCats()
        val catService: CatService by inject()
        addedCat = catService.add(Kitty(name = "Garfield", color = "Orange"))
    }

    private fun getRequestMessageForCatId(id: Int): JsonNode {
        val data = ObjectNode(JsonNodeFactory.instance)
        data.put("catId", id)
        return createReplyNatsMessage(data)
    }

    @Test
    fun `returns queried cat as json`() {
        val response = sendNatsMessageWithReply(SUBJECT, getRequestMessageForCatId(addedCat.id), timeout)
        assertNotNull(response.path("id").asInt())
        assertEquals(addedCat.id, response.path("result").path("id").asInt())
        assertEquals("Garfield", response.path("result").path("name").asText())
        assertEquals("Orange", response.path("result").path("color").asText())
    }

    @Test
    fun `returns error to request without cat id`() {
        val invalidMessage = createReplyNatsMessage(ObjectNode(JsonNodeFactory.instance))
        val response = sendNatsMessageWithReply(SUBJECT, invalidMessage, timeout)
        assertEquals(NatsErrorType.BAD_REQUEST_ERROR.errorName, response.path("error").path("name").asText())
        assertEquals("Request is missing a valid id", response.path("error").path("message").asText())
    }

    @Test
    fun `returns error to request with non existing cat id`() {
        val response = sendNatsMessageWithReply(SUBJECT, getRequestMessageForCatId(123456), timeout)
        assertEquals(NatsErrorType.NOT_FOUND_ERROR.errorName, response.path("error").path("name").asText())
        assertEquals("Couldn't find a cat with id: 123456", response.path("error").path("message").asText())
    }
}
