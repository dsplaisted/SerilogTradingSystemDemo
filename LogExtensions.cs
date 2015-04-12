using RightEdge.Common;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

static class LogExtensions
{
    public static ILogger With(this ILogger logger, Position position)
    {
        return logger.ForContext("Position", position, true)
            .ForContext("PositionID", position.ID);
    }

    public static ILogger With(this ILogger logger, Order order)
    {
        return logger.ForContext("Order", order, true)
            .ForContext("OrderID", order.ID);
    }
}
