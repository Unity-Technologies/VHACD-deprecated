package com.unity.template.db

import com.unity.template.utils.getPropertyOrThrow
import io.ktor.application.Application
import org.flywaydb.core.Flyway
import org.jetbrains.exposed.sql.Database

fun Application.register() {
    val dbUrl: String = getPropertyOrThrow("db.url")
    val dbUsername: String = getPropertyOrThrow("db.username")
    val dbPassword: String = getPropertyOrThrow("db.password")

    Database.connect(dbUrl, driver = "org.postgresql.Driver", user = dbUsername, password = dbPassword)
    Flyway.configure()
        .dataSource(dbUrl, dbUsername, dbPassword)
        .load()
        .migrate()
}
