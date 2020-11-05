package com.unity.nats

import com.unity.template.monitoring.MetricsRegistry
import io.ktor.application.Application
import io.ktor.util.KtorExperimentalAPI
import io.nats.client.Connection
import io.nats.client.Nats
import io.nats.client.Options
import org.koin.core.context.loadKoinModules
import org.koin.dsl.module

const val NATS_SERVICE_SUBJECT_NAME = "ads-kotlin-service-template"

val natsModule = module {
    single { natsConnection(getProperty("nats.servers")) }
    single { NatsJsonMessageHandler(get(), get<MetricsRegistry>().getRegistry()) }
}

@KtorExperimentalAPI
fun Application.register() {
    loadKoinModules(natsModule)
}

private fun natsConnection(natsServers: String): Connection {
    val servers = makeListOfNatsServers(natsServers)
    val buildOptions = Options.Builder()
        .servers(servers.toTypedArray())
        .maxReconnects(-1) // Unlimited reconnects
        .build()
    return Nats.connect(buildOptions)
}

private fun makeListOfNatsServers(natsServers: String): List<String> {
    return natsServers.split(",").map { "nats://$it" }
}
