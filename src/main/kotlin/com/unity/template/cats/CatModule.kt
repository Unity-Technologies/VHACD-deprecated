package com.unity.template.cats

import io.ktor.application.Application
import io.ktor.routing.routing
import org.koin.core.context.loadKoinModules
import org.koin.dsl.module
import org.koin.ktor.ext.get
import org.koin.ktor.ext.inject

val catModule = module {
    single { CatRepository() }
    single { CatService(get()) }
    single { CatSubscription(get(), get()) }
}

fun Application.register() {
    loadKoinModules(catModule)
    val subscription: CatSubscription by inject()
    subscription.subscribe()

    routing {
        cats()
    }
}
