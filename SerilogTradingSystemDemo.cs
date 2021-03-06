#region Using statements
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using RightEdge.Common;
using RightEdge.Common.ChartObjects;
using RightEdge.Indicators;
using Serilog;
#endregion

//  index="serilog" | spath "Properties.Order.Description"| stats count by "Properties.Order.Description"
//  index="serilog" | spath "Properties.PositionID"| spath "Properties.RealizedProfit" | stats sum("Properties.RealizedProfit") by "Properties.PositionID"
//  index="serilog" | spath "Properties.PositionID"| spath "Properties.RealizedProfit" | stats sum("Properties.RealizedProfit") As PositionProfit by "Properties.PositionID" | chart count by "PositionProfit" span=100

public class MySystem : MySystemBase
{
    public ILogger Log { get; private set; }

	public override void Startup()
	{
        Log = new LoggerConfiguration()
            .Destructure.ByTransforming<Order>(o => new
            {
                OrderID = o.ID,
                PositionID = o.BrokerOrder.PositionID,
                Symbol = o.Symbol,
                OrderType = o.OrderType,
                TransactionType = o.TransactionType,
                Size = o.Size,
                LimitPrice = o.LimitPrice,
                StopPrice = o.StopPrice,
                OrderState = o.OrderState,
                Description = o.Description
            })
            .Destructure.ByTransforming<Position>(p => new
            {
                ID = p.ID,
                PositionType = p.Type,
                Symbol = p.Symbol,
                CurrentSize = p.CurrentSize
            })
            .Destructure.ByTransforming<Fill>(f => new
            {
                FilledTime = f.FillDateTime,
                Price = f.Price.SymbolPrice,
                Size = f.Quantity
            })
            .Destructure.AsScalar<Symbol>()
            .Enrich.With(new SimulationTimeEnricher(SystemData))
            .WriteTo.Seq("http://localhost:5341")
            .WriteTo.SplunkViaTcp("127.0.0.1", 10001)
            .CreateLogger()
            .ForContext("SimulationGuid", Guid.NewGuid());

        PositionManager.OrderSubmitted += PositionManager_OrderSubmitted;
	}

    void PositionManager_OrderSubmitted(object sender, OrderUpdatedEventArgs e)
    {
        Log.With(e.Position).With(e.Order).Information("Order submitted: {OrderString}", e.Order);
    }
}


public class MySymbolScript : MySymbolScriptBase
{
    SMA SMAFast;
    SMA SMASlow;

    public override void Startup()
    {
        int param1 = (int)SystemParameters["Param1"];
        double param2 = SystemParameters["Param2"];

        SMAFast = new SMA(param1, Close);
        SMASlow = new SMA((int) (param1 * param2), Close);        
	}

    public override void NewBar()
    {
        if (!double.IsNaN(SMASlow.Current))
        {
            if (SystemUtils.CrossOver(SMAFast, SMASlow) && !OpenPositions.Any())
            {
                PositionSettings positionSettings = new PositionSettings();
                positionSettings.OrderType = OrderType.Market;
                positionSettings.PositionType = PositionType.Long;

                Position pos = OpenPosition(positionSettings);
                if (pos.Error != null)
                {
                    OutputError(pos.Error);
                }
            }
        }
    }

	public override void OrderFilled(Position position, Trade trade)
	{
        double realizedProfit = GetRealizedProfit(position, trade);
        Fill fill = trade.Order.Fills.Last();

        TradingSystem.Log.ForContext("RealizedProfit", realizedProfit)
            .With(position).With(trade.Order)
            .ForContext("Fill", fill, true)
            .Information("Order filled: {OrderString} {FillString}", trade.Order, fill);

        if (trade.TradeType == TradeType.OpenPosition)
        {
            long step1 = (long) Math.Ceiling((double)position.CurrentSize / 2);
            long step2 = position.CurrentSize - step1;

            SubmitScaleOrder(position, step1, TradeType.ProfitTarget, 0.05, 1);
            SubmitScaleOrder(position, step2, TradeType.ProfitTarget, 0.10, 2);
            SubmitScaleOrder(position, step1, TradeType.StopLoss, 0.05, 1);
            SubmitScaleOrder(position, step2, TradeType.StopLoss, 0.10, 2);
        }
	}

    void SubmitScaleOrder(Position position, long size, TradeType tradeType, double percent, int number)
    {
        OrderSettings settings = new OrderSettings();
        settings.Size = size;
        settings.TransactionType = TransactionType.Sell;

        double tickSize = Symbol.SymbolInformation.TickSize;
        if (tickSize == 0 && Symbol.SymbolInformation.DecimalPlaces != 0)
        {
            tickSize = Math.Pow(10, -Symbol.SymbolInformation.DecimalPlaces);
        }

        if (tradeType == TradeType.ProfitTarget)
        {
            settings.OrderType = OrderType.Limit;
            settings.LimitPrice = position.Info.GetTargetPrice(percent, TargetPriceType.RelativeRatio, tradeType);
            if (tickSize != 0)
            {
                settings.LimitPrice = SystemUtils.RoundToNearestHighTick(settings.LimitPrice, tickSize);
            }
            settings.Description = "Profit Target " + number;
        }
        else if (tradeType == TradeType.StopLoss)
        {
            settings.OrderType = OrderType.Stop;
            settings.StopPrice = position.Info.GetTargetPrice(percent, TargetPriceType.RelativeRatio, tradeType);
            if (tickSize != 0)
            {
                settings.StopPrice = SystemUtils.RoundToNearestLowTick(settings.StopPrice, tickSize);
            }
            settings.Description = "Stop Loss " + number;
        }
        else
        {
            throw new ArgumentException("Unexpected trade type: " + tradeType);
        }

        settings.BarsValid = -1;

        Order order = position.SubmitOrder(settings);
        if (order.Error != null)
        {
            OutputError(order.Error);
        }
    }

	public override void OrderCancelled(Position position, Order order, string information)
	{
		// This method is called when an order is cancelled or rejected
        if (!order.CancelPending)
        {
            OutputWarning("Unexpected order cancel: " + order.ToString() + " " + information);
            TradingSystem.Log.With(position).With(order).Warning("Unexpected order cancel for {OrderString}: {Information}", order, information);
        }
	}

    private double GetRealizedProfit(Position position, Trade trade)
    {
        double realizedProfit;
        if (trade.TradeIndex == 0)
        {
            realizedProfit = trade.PositionStats.RealizedProfit;
        }
        else
        {
            var prevStats = position.Trades[trade.TradeIndex - 1].PositionStats;
            realizedProfit = trade.PositionStats.RealizedProfit - prevStats.RealizedProfit;
        }
        return realizedProfit;
    }
}
