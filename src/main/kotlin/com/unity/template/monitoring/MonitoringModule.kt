package com.unity.template.monitoring

import io.ktor.application.Application
import io.ktor.application.call
import io.ktor.response.respond
import io.ktor.routing.Routing
import io.ktor.routing.get
import io.ktor.routing.routing
import io.micrometer.core.instrument.Metrics
import io.micrometer.core.instrument.binder.MeterBinder
import io.micrometer.core.instrument.binder.jvm.ClassLoaderMetrics
import io.micrometer.core.instrument.binder.jvm.JvmGcMetrics
import io.micrometer.core.instrument.binder.jvm.JvmMemoryMetrics
import io.micrometer.core.instrument.binder.jvm.JvmThreadMetrics
import io.micrometer.core.instrument.binder.logging.LogbackMetrics
import io.micrometer.core.instrument.binder.system.ProcessorMetrics
import io.micrometer.core.instrument.binder.system.UptimeMetrics
import io.micrometer.prometheus.PrometheusConfig
import io.micrometer.prometheus.PrometheusMeterRegistry
import io.prometheus.client.Counter
import io.prometheus.client.Histogram
import org.koin.core.context.loadKoinModules
import org.koin.dsl.module
import org.koin.ktor.ext.inject

val monitoringModule = module {
    single {
        MetricsRegistry(
            listOf(
                ClassLoaderMetrics(),
                JvmMemoryMetrics(),
                JvmThreadMetrics(),
                JvmGcMetrics(),
                ProcessorMetrics(),
                UptimeMetrics(),
                LogbackMetrics()
            )
        )
    }
}

fun Application.register() {
    loadKoinModules(monitoringModule)

    routing {
        metrics()
    }
}

fun Routing.metrics() {
    val restEndpoint = "/metrics"
    val metricRegistry: MetricsRegistry by inject()

    get(restEndpoint) {
        call.respond(metricRegistry.getMetrics())
    }
}

class MetricsRegistry(metrics: List<MeterBinder>) {
    private val registry = PrometheusMeterRegistry(PrometheusConfig.DEFAULT)

    init {
        metrics.forEach { it.bindTo(registry) }
        Metrics.addRegistry(registry)
    }

    fun getRegistry(): PrometheusMeterRegistry = registry

    fun getMetrics(): String = registry.scrape()
}

interface MonitoringMetric {
    val metricName: String
    val description: String
}

fun initCounter(
    metric: MonitoringMetric,
    meterRegistry: PrometheusMeterRegistry,
    vararg labelNames: String
): Counter {
    return Counter
        .build()
        .name(metric.metricName)
        .help(metric.description)
        .labelNames(* labelNames)
        .register(meterRegistry.prometheusRegistry)
}

fun initHistogram(
    metric: MonitoringMetric,
    meterRegistry: PrometheusMeterRegistry,
    vararg labelNames: String
): Histogram {
    return Histogram
        .build()
        .name(metric.metricName)
        .help(metric.description)
        .labelNames(* labelNames)
        .register(meterRegistry.prometheusRegistry)
}
