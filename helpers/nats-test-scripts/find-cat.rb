#!/usr/bin/ruby
require "nats/io/client"
require "json"
require "pp"

nats = NATS::IO::Client.new
#Localhost:
nats.connect(:servers => ['nats://127.0.0.1:4222'])

data = {
    "correlationId" => "98765-123456-6667-362519",
    "params" => {
        "userId" => "123"
    },
    "data" => {
        "catId" => "1"
    }
}

puts "Sending:"
pp data
response = nats.request('find.ads-kotlin-service-template.cats', data.to_json, timeout: 10)
puts "Got a response: '#{response}'"
puts JSON.parse(response.data).to_json
nats.close
