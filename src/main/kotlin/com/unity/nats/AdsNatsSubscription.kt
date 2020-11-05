package com.unity.nats

import com.fasterxml.jackson.databind.JsonNode

interface AdsNatsSubscription<T> {
    val subject: String
    fun subscribe()
}

interface AdsNatsSubscriptionWithAck<T> : AdsNatsSubscription<T> {
    val ackSubject: String
    val retrySubject: String
    fun handleJsonMessage(entity: T)
}

interface AdsNatsSubscriptionWithReply<T> : AdsNatsSubscription<T> {
    fun handleJsonMessage(entity: T): JsonNode
}
