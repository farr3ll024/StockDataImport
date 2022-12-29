﻿using EodHistoricalData.Sdk;
using EodHistoricalData.Sdk.Models;
using Import.Infrastructure.Abstractions;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;
using static Import.Infrastructure.Configuration.Constants;

namespace Import.AppServices
{
    public sealed class DataImportService
    {
        private readonly ILogsDbContext logsDbContext;
        private readonly IImportsDbContext importsDbContext;
        private readonly IDataClient dataClient;
        private readonly ILogger? logger;
        private readonly HashSet<Symbol> allSymbols;

        public delegate void ApiLimitReachedHandler(object sender, ApiLimitReachedException apiLimitReachedException);

        public event ApiLimitReachedHandler? ApiLimitReachedEventHandler;

        public delegate void ApiResponseExceptionHandler(object sender,
            ApiResponseException apiResponseException,
            string[] symbols);

        public event ApiResponseExceptionHandler? ApiResponseExceptionEventHandler;

        public delegate void CommunicationHandler(object sender, string message);

        public event CommunicationHandler? CommunicationEventHandler;

        internal DataImportService(ILogsDbContext logsDbContext,
            IImportsDbContext importsDbContext,
            string apiKey,
            int maxusage = 100_000,
            ILogger? logger = null)
            : this(logsDbContext, importsDbContext, new DataClient(apiKey, logger), maxusage, logger)
        { }

        internal DataImportService(ILogsDbContext logsDbContext,
            IImportsDbContext importsDbContext,
            IDataClient dataClient,
            int maxUsage = 100_000,
            ILogger? logger = null)
        {
            this.logsDbContext = logsDbContext;
            this.importsDbContext = importsDbContext;
            this.dataClient = dataClient;
            this.dataClient.ApiResponseExceptionEventHandler += DataClient_ApiResponseExceptionEventHandler;
            this.dataClient.CommunicationEventHandler += DataClient_CommunicationEventHandler;
            this.dataClient.ApiLimitReachedEventHandler += DataClient_ApiLimitReachedEventHandler;
            _ = dataClient.ResetUsageAsync(maxUsage).GetAwaiter().GetResult();
            this.logger = logger;
            allSymbols = new();
        }

        private void DataClient_ApiLimitReachedEventHandler(object sender, ApiLimitReachedException apiLimitReachedException)
        {
            ApiLimitReachedEventHandler?.Invoke(sender, apiLimitReachedException);
        }

        public static int Usage => ApiService.Usage;

        public static int DailyLimit => ApiService.DailyLimit;

        public Task PurgeDataAsync(string purgeName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (purgeName == PurgeName.Logs)
            {
                return logsDbContext.PurgeLogsAsync(cancellationToken);
            }

            if (purgeName == PurgeName.ActionLogs)
            {
                return logsDbContext.PurgeActionLogsAsync(cancellationToken);
            }

            if (purgeName == PurgeName.Imports)
            {
                return importsDbContext.PurgeAsync(cancellationToken);
            }

            return Task.CompletedTask;
        }

        public async Task TruncateLogsAsync(string logLevel, DateTime date, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (date > DateTime.UtcNow) { await Task.CompletedTask; }

            await logsDbContext.TruncateLogsAsync(logLevel, date, cancellationToken);
        }

        public Task ImportDataAsync(string scope, string exchange, string dataType, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Communicate($"Importing\t{scope} {exchange} {dataType}");

            if (dataType == DataTypes.Symbols)
            {
                var symbols = dataClient.GetSymbolListAsync(exchange, cancellationToken)
                    .GetAwaiter().GetResult()
                    .ToArray();

                allSymbols.UnionWith(symbols);

                return importsDbContext.SaveSymbolsAsync(symbols, cancellationToken);
            }
            else if (scope == DataTypeScopes.Full)
            {
                return ImportFullAsync(exchange, dataType, cancellationToken);
            }
            else if (scope == DataTypeScopes.Bulk)
            {
                return ImportBulkAsync(exchange, dataType, cancellationToken);
            }

            return Task.CompletedTask;
        }

        public async Task ApplyFixAsync(string name, CancellationToken cancellationToken)
        {
            if (name.Equals("has options", StringComparison.OrdinalIgnoreCase))
            {
                var symbols = File.ReadAllLines("Fixes/OptionableSymbols.txt")
                    .Select(s => s.Trim());

                foreach (var chunk in symbols.Chunk(500))
                {
                    await importsDbContext.SetOptionableOnSymbolsAsync(chunk, cancellationToken);
                }
            }
        }

        private Task ImportFullAsync(string exchange, string dataType, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (exchange == dataType && dataType == DataTypes.Exchanges) // body Exchange and DateType are "Exchanges"
            {
                int estimatedCost = ApiService.GetCost(ApiService.ExchangesUri);
                if (estimatedCost > ApiService.Available)
                {
                    ApiLimitReachedEventHandler?.Invoke(this, new ApiLimitReachedException(
                        $"exchange list", ApiService.Usage, estimatedCost + ApiService.Usage));
                }
                else
                {
                    return importsDbContext.SaveExchangesAsync(dataClient.GetExchangeListAsync(cancellationToken).GetAwaiter().GetResult(),
                        cancellationToken);
                }

                return Task.CompletedTask;
            }

            if (!allSymbols.Any())
            {
                allSymbols.UnionWith(importsDbContext.GetAllSymbolsAsync(cancellationToken).GetAwaiter().GetResult());
            }

            var symbolsForExchange = allSymbols.Where(s => s.Exchange == exchange).ToArray();

            if (dataType == DataTypes.Splits)
            {
                int estimatedCost = ApiService.GetCost(ApiService.SplitsUri, symbolsForExchange.Length);
                if (estimatedCost > ApiService.Available)
                {
                    ApiLimitReachedEventHandler?.Invoke(this, new ApiLimitReachedException(
                        $"splits for {exchange}", ApiService.Usage, estimatedCost + ApiService.Usage));
                }
                else
                {
                    return SaveSplitsAsync(symbolsForExchange, cancellationToken);
                }
            }

            if (dataType == DataTypes.Dividends)
            {
                int estimatedCost = ApiService.GetCost(ApiService.DividendUri, symbolsForExchange.Length);
                if (estimatedCost > ApiService.Available)
                {
                    ApiLimitReachedEventHandler?.Invoke(this, new ApiLimitReachedException(
                        $"dividends for {exchange}", ApiService.Usage, estimatedCost + ApiService.Usage));
                }
                else
                {
                    return SaveDividendsAsync(symbolsForExchange, cancellationToken);
                }
            }

            if (dataType == DataTypes.Prices)
            {
                int estimatedCost = ApiService.GetCost(ApiService.EodUri, symbolsForExchange.Length);
                if (estimatedCost > ApiService.Available)
                {
                    ApiLimitReachedEventHandler?.Invoke(this, new ApiLimitReachedException(
                        $"prices for {exchange}", ApiService.Usage, estimatedCost + ApiService.Usage));
                }
                else
                {
                    return SavePricesAsync(symbolsForExchange, cancellationToken);
                }
            }

            if (dataType == DataTypes.Options)
            {
                var symbolsWithOptions = importsDbContext.GetSymbolsWithOptionsAsync(cancellationToken)
                    .GetAwaiter().GetResult().ToArray();

                if (symbolsWithOptions.Any())
                {
                    int estimatedCost = ApiService.GetCost(ApiService.EodUri, symbolsForExchange.Length);
                    if (estimatedCost > ApiService.Available)
                    {
                        ApiLimitReachedEventHandler?.Invoke(this, new ApiLimitReachedException(
                            $"options for {exchange}", ApiService.Usage, estimatedCost + ApiService.Usage));
                    }
                    else
                    {
                        return SaveOptionsAsync(symbolsWithOptions, cancellationToken);
                    }
                }
            }

            if (dataType == DataTypes.Fundamentals)
            {
                // TODO: lots to do here.
            }

            return Task.CompletedTask;
        }

        private async Task SaveSplitsAsync(Symbol[] symbols, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var symbol in symbols)
            {
                var splits = (await dataClient.GetSplitsForSymbolAsync(symbol.Code, cancellationToken: cancellationToken)).ToList();

                List<Infrastructure.Domain.Split> domainSplits = new();

                splits.ForEach(s => domainSplits.Add(new Infrastructure.Domain.Split(symbol.Code, symbol.Exchange, s)));

                await importsDbContext.SaveSplitsAsync(domainSplits, cancellationToken);
            }
        }

        private async Task SaveDividendsAsync(Symbol[] symbols, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var symbol in symbols)
            {
                var divs = await dataClient.GetDividendsForSymbolAsync(symbol.Code, cancellationToken: cancellationToken);

                await importsDbContext.SaveDividendsAsync(symbol.Code, symbol.Exchange, divs, cancellationToken);
            }
        }

        private async Task SavePricesAsync(Symbol[] symbols, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var symbol in symbols)
            {
                var prices = await dataClient.GetPricesForSymbolAsync(symbol.Code, cancellationToken: cancellationToken);
                await importsDbContext.SavePriceActionsAsync(symbol.Code, symbol.Exchange, prices, cancellationToken);
            }
        }

        private async Task SaveOptionsAsync(Symbol[] symbols, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var symbol in symbols)
            {
                var options = await dataClient.GetOptionsForSymbolAsync(symbol.Code, cancellationToken: cancellationToken);

                await importsDbContext.SaveOptionsAsync(options, cancellationToken);
            }
        }

        private Task ImportBulkAsync(string exchange, string dataType, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (exchange == dataType && dataType == DataTypes.Exchanges)
            {
                var modelExchanges = dataClient.GetExchangeListAsync(cancellationToken).GetAwaiter().GetResult();

                return importsDbContext.SaveExchangesAsync(modelExchanges, cancellationToken);
            }

            return Task.CompletedTask;
        }

        private void HandleApiResponseException(ApiResponseException exc, string[] symbols)
        {
            ApiResponseExceptionEventHandler?.Invoke(this, exc, symbols);
        }

        private void Communicate(string message)
        {
            CommunicationEventHandler?.Invoke(this, message);
        }

        private void DataClient_CommunicationEventHandler(object sender, string message)
        {
            CommunicationEventHandler?.Invoke(sender, message);
        }

        private void DataClient_ApiResponseExceptionEventHandler(object sender, ApiResponseException apiResponseException, string[] symbols)
        {
            ApiResponseExceptionEventHandler?.Invoke(sender, apiResponseException, symbols);
        }
    }
}
