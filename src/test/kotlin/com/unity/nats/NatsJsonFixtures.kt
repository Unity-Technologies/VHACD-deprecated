package com.unity.nats

import com.fasterxml.jackson.databind.JsonNode
import com.fasterxml.jackson.databind.ObjectMapper
import com.fasterxml.jackson.databind.node.ObjectNode
import com.fasterxml.jackson.module.kotlin.registerKotlinModule

private val OBJECT_MAPPER = ObjectMapper().registerKotlinModule()

object NatsJsonFixtures {
    fun createReplyNatsMessage(data: JsonNode): JsonNode {
        val message = OBJECT_MAPPER.readTree(
            """
            {
              "id": "12345-123456-1234",
              "correlationId": "98765-123456-6667-362519",
              "params": {}
            }
            """
        ) as ObjectNode

        message.replace("data", data)
        return message
    }
}
