package com.unity.nats

open class NatsAwareException(
    val natsErrorType: NatsErrorType,
    message: String
) : Exception(message)

// names to match mappings in https://github.com/Applifier/ads-common-gateway/blob/master/routes/errors.yml
enum class NatsErrorType(val errorName: String) {
    BAD_REQUEST_ERROR("BadRequestError"),
    NOT_FOUND_ERROR("NotFoundError"),
    UNEXPECTED_ERROR("UnexpectedError");

    fun errorName(): String {
        return errorName
    }
}
