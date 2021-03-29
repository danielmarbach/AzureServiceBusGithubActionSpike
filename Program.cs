﻿using System;
using System.Threading.Tasks;
using static System.Console;
using static System.Text.Encoding;

namespace Receive
{
    using Azure.Messaging.ServiceBus;
    using Azure.Messaging.ServiceBus.Administration;

    internal class Program
    {
        private static readonly string connectionString =
            Environment.GetEnvironmentVariable("AZURESERVICEBUS_CONNECTIONSTRING");

        private static readonly string destination = "queue";

        private static readonly TaskCompletionSource<bool> syncEvent =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private static async Task Main(string[] args)
        {
            var client = new ServiceBusAdministrationClient(connectionString);
            if (!await client.QueueExistsAsync($"{destination}")){
                     await client.CreateQueueAsync($"{destination}");
            }
            
            for (int i = 0; i < 100; i++)
            {
                if (!await client.QueueExistsAsync($"{destination}{i}")){
                     await client.CreateQueueAsync($"{destination}{i}");
                }
            }

            await using var serviceBusClient = new ServiceBusClient(connectionString, new ServiceBusClientOptions
            {
                RetryOptions = new ServiceBusRetryOptions
                {
                    TryTimeout = TimeSpan.FromSeconds(2)
                }
            });

            await using var sender = serviceBusClient.CreateSender(destination);
            await sender.SendMessageAsync(new ServiceBusMessage(UTF8.GetBytes("Deep Dive")));
            WriteLine("Message sent");

            var processorOptions = new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 1,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                PrefetchCount = 10
            };

            await using var receiver = serviceBusClient.CreateProcessor(destination, processorOptions);
            receiver.ProcessMessageAsync += async messageEventArgs =>
            {
                var message = messageEventArgs.Message;
                await Out.WriteLineAsync(
                    $"Received message with '{message.MessageId}' and content '{UTF8.GetString(message.Body)}'");
                // throw new InvalidOperationException();
                await messageEventArgs.CompleteMessageAsync(message);
                syncEvent.TrySetResult(true);
            };
            receiver.ProcessErrorAsync += async errorEventArgs =>
            {
                await Out.WriteLineAsync($"Exception: {errorEventArgs.Exception}");
                await Out.WriteLineAsync($"FullyQualifiedNamespace: {errorEventArgs.FullyQualifiedNamespace}");
                await Out.WriteLineAsync($"ErrorSource: {errorEventArgs.ErrorSource}");
                await Out.WriteLineAsync($"EntityPath: {errorEventArgs.EntityPath}");
            };

            await receiver.StartProcessingAsync();

            await syncEvent.Task;

            await receiver.StopProcessingAsync();
        }
    }
}