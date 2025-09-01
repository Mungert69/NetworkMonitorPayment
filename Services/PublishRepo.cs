using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils;

namespace NetworkMonitor.Objects.Repository
{
    public class PublishRepo
    {
        // ----------------- Public API (unchanged signatures/behavior) -----------------

        public static async Task GetProductsAsync(
            ILogger logger,
            List<IRabbitRepo> rabbitRepos,
            UpdateProductObj updateProductObj)
        {
         
            // publish to all systems in parallel
            var tasks = rabbitRepos.Select(async repo =>
            {
                try
                {
                await repo.PublishAsync("getProducts", null);
                    logger.LogInformation("Published event getProducts to: {Target}", repo.SystemUrl);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed publishing getProducts to: {Target}", repo.SystemUrl);
                }
            });

            await Task.WhenAll(tasks);
        }

        public static async Task PaymentReadyAsync(
            ILogger logger,
            List<IRabbitRepo> rabbitRepos,
            bool isReady)
        {
            var payload = new PaymentServiceInitObj { IsPaymentServiceReady = isReady };

            // publish to all systems in parallel
            var tasks = rabbitRepos.Select(async repo =>
            {
                try
                {
                    await repo.PublishAsync("paymentServiceReady", payload);
                    logger.LogInformation("Published event paymentServiceReady (IsReady={IsReady}) to: {Target}",
                        isReady, repo.SystemUrl);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed publishing paymentServiceReady to: {Target}", repo.SystemUrl);
                }
            });

            await Task.WhenAll(tasks);
        }

        public static async Task<bool> UpdateUserSubscriptionAsync(
            ILogger logger,
            List<IRabbitRepo> rabbitRepos,
            PaymentTransaction paymentTransaction)
        {
            return await PublishToTarget(logger, rabbitRepos, paymentTransaction, "updateUserSubscription");
        }

        public static async Task<bool> BoostTokenForUserAsync(
            ILogger logger,
            List<IRabbitRepo> rabbitRepos,
            PaymentTransaction paymentTransaction)
        {
            return await PublishToTarget(logger, rabbitRepos, paymentTransaction, "boostTokenForUser");
        }

        public static async Task<bool> UpdateUserPingInfosAsync(
            ILogger logger,
            List<IRabbitRepo> rabbitRepos,
            PaymentTransaction paymentTransaction)
        {
            return await PublishToTarget(logger, rabbitRepos, paymentTransaction, "updateUserPingInfos");
        }

        public static async Task<bool> UpdateUserCustomerIdAsync(
            ILogger logger,
            List<IRabbitRepo> rabbitRepos,
            PaymentTransaction paymentTransaction)
        {
            return await PublishToTarget(logger, rabbitRepos, paymentTransaction, "updateUserCustomerId");
        }

        // ----------------- Internals (helpers only; routing unchanged) -----------------

        private static async Task<bool> PublishToTarget(
            ILogger logger,
            List<IRabbitRepo> rabbitRepos,
            PaymentTransaction tx,
            string eventName)
        {
            try
            {
                if (tx == null)
                {
                    logger.LogError("Publish {Event} failed: PaymentTransaction is null", eventName);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(tx.ExternalUrl))
                {
                    logger.LogError("Publish {Event} failed: ExternalUrl is empty (txId={TxId}, evtId={EvtId})",
                        eventName, tx.Id, tx.EventId);
                    return false;
                }

                var repo = FindRepoByExternalUrl(rabbitRepos, tx.ExternalUrl);
                if (repo == null)
                {
                    logger.LogError("Error: RabbitRepo for {ExternalUrl} cannot be found (event={Event}, txId={TxId}, evtId={EvtId})",
                        tx.ExternalUrl, eventName, tx.Id, tx.EventId);
                    return false;
                }

                await repo.PublishAsync(eventName, tx);
                logger.LogInformation(
                    "Published {Event} for cust={CustomerId} user={UserId} evtId={EvtId} to {Target}",
                    eventName, tx.UserInfo?.CustomerId, tx.UserInfo?.UserID, tx.EventId, repo.SystemUrl);

                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in PublishRepo.{Event}. evtId={EvtId}", eventName, tx?.EventId);
                return false;
            }
        }

        private static IRabbitRepo? FindRepoByExternalUrl(
            IEnumerable<IRabbitRepo> repos,
            string externalUrl)
        {
            // EXACT match on ExternalUrl (no normalization) to preserve current behavior.
            return repos.FirstOrDefault(r => r.SystemUrl.ExternalUrl == externalUrl);
        }
    }
}
