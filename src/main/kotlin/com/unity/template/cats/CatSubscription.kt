package com.unity.template.cats

import com.fasterxml.jackson.annotation.JsonIgnoreProperties
import com.fasterxml.jackson.annotation.JsonInclude
import com.fasterxml.jackson.annotation.JsonProperty
import com.fasterxml.jackson.databind.JsonNode
import com.fasterxml.jackson.databind.ObjectMapper
import com.fasterxml.jackson.module.kotlin.registerKotlinModule
import com.unity.nats.AdsNatsSubscriptionWithReply
import com.unity.nats.NATS_SERVICE_SUBJECT_NAME
import com.unity.nats.NatsAwareException
import com.unity.nats.NatsChannelVerb
import com.unity.nats.NatsErrorType
import com.unity.nats.NatsJsonMessageHandler
import mu.KotlinLogging

private val LOGGER = KotlinLogging.logger {}
val OBJECT_MAPPER = ObjectMapper().registerKotlinModule()

internal const val RESOURCE = "cats"

@JsonIgnoreProperties(ignoreUnknown = true)
data class CatRequest(@JsonProperty val data: Data) {

    data class Data(@JsonProperty val catId: Int)

    fun catId(): Int {
        return data.catId
    }
}

@JsonIgnoreProperties(ignoreUnknown = true)
data class CatResponse(@JsonProperty val result: Result) {
    @JsonInclude(JsonInclude.Include.NON_NULL)
    data class Result(
        @JsonProperty var id: String,
        @JsonProperty var name: String,
        @JsonProperty val color: String
    )
}

class CatSubscription(
    private val jsonMessageHandler: NatsJsonMessageHandler,
    private val catService: CatService
) : AdsNatsSubscriptionWithReply<CatRequest> {
    override val subject = "${NatsChannelVerb.FIND.nameInSubject()}.$NATS_SERVICE_SUBJECT_NAME.$RESOURCE"

    override fun subscribe() {
        jsonMessageHandler.registerHandler(this)
    }

    override fun handleJsonMessage(entity: CatRequest): JsonNode {
        LOGGER.info { "Received message with contents: $entity" }
        if (entity.catId() < 1) {
            throw MissingIdException()
        }

        try {
            val result = catService.find(entity.catId())
            return createReplyJson(toResponse(result))
        } catch (e: Exception) {
            throw EntityNotFoundException("Couldn't find a cat with id: ${entity.catId()}")
        }
    }

    companion object {
        fun createReplyJson(response: CatResponse): JsonNode {
            return OBJECT_MAPPER.valueToTree(response)
        }

        fun toResponse(cat: Kitty): CatResponse = CatResponse(
            CatResponse.Result(cat.id.toString(), cat.name, cat.color)
        )
    }
}

class EntityNotFoundException(reason: String) :
    NatsAwareException(NatsErrorType.NOT_FOUND_ERROR, reason)

class MissingIdException :
    NatsAwareException(NatsErrorType.BAD_REQUEST_ERROR, "Request is missing a valid id")
