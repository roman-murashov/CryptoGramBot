﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoGramBot.Data;
using CryptoGramBot.Helpers;
using CryptoGramBot.Models;
using Microsoft.Extensions.Logging;

namespace CryptoGramBot.Services
{
    public class DatabaseService
    {
        private readonly CryptoGramBotDbContext _context;
        private readonly Dictionary<string, BalanceHistory> _lastBalances = new Dictionary<string, BalanceHistory>();
        private readonly ILogger<DatabaseService> _log;

        public DatabaseService(ILogger<DatabaseService> log, CryptoGramBotDbContext context)
        {
            _log = log;
            _context = context;
        }

        public async Task<BalanceHistory> AddBalance(decimal balance, decimal dollarAmount, string name)
        {
            var balanceHistory = new BalanceHistory
            {
                DateTime = DateTime.Now,
                Balance = balance,
                DollarAmount = dollarAmount,
                Name = name
            };

            _log.LogInformation($"Adding balance to database: {name} - {balance}");

            await SaveBalance(balanceHistory, name);

            return balanceHistory;
        }

        public void AddCoinigyAccounts(Dictionary<int, CoinigyAccount> coinigyAccounts)
        {
            //            throw new NotImplementedException();
        }

        public async Task AddLastChecked(string exchange, DateTime timestamp)
        {
            var lastCheckeds = _context.LastCheckeds;
            var lastChecked = lastCheckeds.SingleOrDefault(x => x.Exchange == exchange);

            if (lastChecked == null)
            {
                lastCheckeds.Add(new LastChecked
                {
                    Exchange = exchange,
                    Timestamp = timestamp
                });
            }
            else
            {
                lastChecked.Timestamp = timestamp;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<List<Trade>> AddTrades(IEnumerable<Trade> trades)
        {
            var newTrades = new List<Trade>();
            _log.LogInformation("Adding new trades to database");

            foreach (var trade in trades)
            {
                var singleOrDefault = _context.Trades.SingleOrDefault(
                    x => x.TimeStamp == trade.TimeStamp &&
                    x.Base == trade.Base &&
                    x.Exchange == trade.Exchange &&
                    x.Quantity == trade.Quantity &&
                    x.QuantityRemaining == trade.QuantityRemaining &&
                    x.Terms == trade.Terms &&
                    x.Cost == trade.Cost
                    );

                if (singleOrDefault == null)
                {
                    _context.Trades.Add(trade);
                    newTrades.Add(trade);
                }
            }

            await _context.SaveChangesAsync();

            _log.LogInformation($"Added {newTrades.Count} new trades to database");
            return newTrades;
        }

        public async Task AddWalletBalances(List<WalletBalance> walletBalances)
        {
            var walletBalancesDb = _context.WalletBalances;
            walletBalancesDb.AddRange(walletBalances);
            await _context.SaveChangesAsync();
        }

        public IEnumerable<BalanceHistory> GetAllBalances()
        {
            var all = _context.BalanceHistories;
            return all.AsEnumerable();
        }

        public IEnumerable<LastChecked> GetAllLastChecked()
        {
            var all = _context.LastCheckeds;
            return all.AsEnumerable();
        }

        public IEnumerable<string> GetAllPairs()
        {
            var collection = _context.Trades;
            var distinct = collection.Select(x => x.Terms).Distinct().OrderBy(x => x);
            return distinct;
        }

        public IEnumerable<ProfitAndLoss> GetAllProfitAndLoss()
        {
            var all = _context.ProfitAndLosses;
            return all.AsEnumerable();
        }

        public IEnumerable<Trade> GetAllTrades()
        {
            var all = _context.Trades;
            return all.AsEnumerable();
        }

        public IEnumerable<Trade> GetAllTradesFor(string term)
        {
            var collection = _context.Trades;
            var trades = collection.Where(x => x.Terms == term).AsEnumerable();
            return trades;
        }

        public BalanceHistory GetBalance24HoursAgo(string name)
        {
            var dateTime = DateTime.Now - TimeSpan.FromHours(24);
            BalanceHistory hour24Balance;

            var histories = _lastBalances.Values.Where(x => x.DateTime.Hour == dateTime.Hour &&
                                                            x.DateTime.Day == dateTime.Day &&
                                                             x.DateTime.Month == dateTime.Month &&
                                                             x.DateTime.Year == dateTime.Year &&
                                                             x.Name == name)
                                                             .ToList();

            if (histories.Count == 0)
            {
                _log.LogInformation($"Retrieving 24 hour balance from database for: {name}");

                var collection = _context.BalanceHistories;
                var balanceHistories = collection.Where(x => x.Name == name).OrderByDescending(x => x.DateTime).ToList();

                histories = balanceHistories.FindAll(x => x.DateTime.Hour == dateTime.Hour &&
                                x.DateTime.Day == dateTime.Day &&
                                x.DateTime.Month == dateTime.Month &&
                                x.DateTime.Year == dateTime.Year)
                                .ToList();

                if (!histories.Any())
                {
                    _log.LogWarning($"Could not find a 24 hour balance for: {name}");
                    hour24Balance = new BalanceHistory
                    {
                        Balance = 0,
                        DollarAmount = 0,
                        Name = name
                    };
                    return hour24Balance;
                }
            }

            var orderByDescending = histories.OrderByDescending(x => x.DateTime);
            hour24Balance = orderByDescending.FirstOrDefault();

            return hour24Balance;
        }

        public List<Trade> GetBuysForPairAndQuantity(decimal sellPrice, decimal quantity, string baseCcy, string terms)
        {
            var contextTrades = _context.Trades;
            var enumerable = contextTrades
                .Where(x => x.Base == baseCcy && x.Terms == terms)
                .AsEnumerable();

            var onlyBuys = enumerable.Where(x => x.Side == TradeSide.Buy);

            var tradesForPair = onlyBuys.OrderByDescending(x => x.TimeStamp);

            var trades = new List<Trade>();

            var quanityChecked = 0m;
            foreach (var trade in tradesForPair)
            {
                if (quanityChecked >= quantity) continue;

                trades.Add(trade);
                quanityChecked = quanityChecked + trade.QuantityOfTrade;
            }
            return trades;
        }

        public DateTime GetLastChecked(string exchange)
        {
            var contextLastCheckeds = _context.LastCheckeds;
            var lastChecked = contextLastCheckeds
                .SingleOrDefault(x => x.Exchange == exchange);

            return lastChecked?.Timestamp ?? Constants.DateTimeUnixEpochStart;
        }

        public Trade GetLastTradeForPair(string currency, string exchange, TradeSide side)
        {
            var contextTrades = _context.Trades;
            var enumerable = contextTrades
                .Where(x => x.Terms == currency && x.Exchange == exchange)
                .AsEnumerable()
                .OrderByDescending(x => x.TimeStamp);

            var onlyBuys = enumerable.Where(x => x.Side == TradeSide.Buy);
            var lastTrade = onlyBuys.FirstOrDefault();

            return lastTrade;
        }

        public Setting GetSetting(string name)
        {
            var allSettings = _context.Settings;
            var singleOrDefault = allSettings.SingleOrDefault(x => x.Name == name);
            return singleOrDefault;
        }

        public IEnumerable<Trade> GetTradesForPair(string ccy1, string ccy2)
        {
            var contextTrades = _context.Trades;
            var enumerable = contextTrades
                .Where(x => x.Base == ccy1 && x.Terms == ccy2)
                .AsEnumerable();

            return enumerable;
        }

        public async Task SaveProfitAndLoss(ProfitAndLoss pnl)
        {
            _log.LogInformation($"Adding pnl for {pnl.Pair} to database");

            var contextProfitAndLosses = _context.ProfitAndLosses;
            contextProfitAndLosses.Add(pnl);

            await _context.SaveChangesAsync();
        }

        public void SaveSetting(Setting setting)
        {
            var contextSettings = _context.Settings;

            var singleOrDefault = contextSettings.SingleOrDefault(x => x.Name == setting.Name);

            if (singleOrDefault == null)
            {
                _context.Settings.Add(setting);
            }
        }

        private async Task SaveBalance(BalanceHistory balanceHistory, string name)
        {
            var balanceHistories = _context.BalanceHistories;
            balanceHistory.Name = name;
            balanceHistories.Add(balanceHistory);
            _context.BalanceHistories.Add(balanceHistory);
            _log.LogInformation($"Saved new balance in database for: {name}");
            _log.LogInformation("Adding balance to cache");
            _lastBalances[name] = balanceHistory;

            await _context.SaveChangesAsync();
        }
    }
}