﻿using RightEdge.Common;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

class SimulationTimeEnricher : ILogEventEnricher
{
    SystemData _systemData;

    public SimulationTimeEnricher(SystemData systemData)
    {
        _systemData = systemData;
    }

    public void Enrich(Serilog.Events.LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SimulationTime", _systemData.CurrentDate));
    }
}