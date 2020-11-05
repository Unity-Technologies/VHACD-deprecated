package com.unity.template

import com.typesafe.config.ConfigFactory
import com.unity.template.cats.Cats
import com.unity.template.utils.CustomApplicationConfig
import io.ktor.server.testing.TestApplicationEngine
import io.ktor.server.testing.createTestEnvironment
import io.ktor.util.KtorExperimentalAPI
import org.jetbrains.exposed.sql.deleteAll
import org.jetbrains.exposed.sql.transactions.transaction
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.BeforeEach
import org.koin.core.context.stopKoin
import org.koin.test.KoinTest

@KtorExperimentalAPI
open class IntegrationTest : KoinTest {
    protected lateinit var engine: TestApplicationEngine

    @BeforeEach
    internal fun setUpEngine() {
        engine = setupTestEngineWithConfiguration("application-test.conf")
    }

    @AfterEach
    internal fun tearDown() {
        transaction {
            // delete all test data
            Cats.deleteAll()
        }

        stopKoin()
    }

    private fun setupTestEngineWithConfiguration(path: String): TestApplicationEngine {
        val engine = TestApplicationEngine(
            createTestEnvironment {
                config = CustomApplicationConfig(ConfigFactory.load(path))
            }
        )
        engine.start(true)
        return engine
    }
}
