package com.unity.nats

enum class NatsChannelVerb {
    // Info on RESTful verbs can be found at https://github.com/Applifier/node-nats-tools#subjects

    PUB,
    FIND,
    CREATE,
    TRIGGER;

    fun nameInSubject(): String {
        return name.toLowerCase()
    }
}
