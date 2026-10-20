using System;
using System.Collections.Generic;
using System.Linq;
using FuelQ.Distributions;
using FuelQ.Models;

namespace FuelQ.Simulation;

/// <summary>
/// Simulation-table engine for an s-server FCFS queue (multi-server Lindley recursion):
/// each vehicle, in arrival order, takes the pump that frees up earliest.
/// Every interarrival and service time is produced by inverse transform from the
/// random number shown in the table, so each row can be reproduced by hand.
/// </summary>
public static class GeneralDESimulator
{
    // Bounds keep inverse transforms finite after rounding R to 4 decimals
    private const double MinR = 0.0001, MaxR = 0.9999;

    public static DESSimulationResult Run(DESSimConfig cfg)
    {
        var rng = new Random(cfg.RandomSeed);
        int numServers = Math.Max(1, cfg.NumServers);
        int maxCustomers = Math.Max(1, cfg.CustomerLimit);
        double maxDuration = cfg.DurationLimit > 0 ? cfg.DurationLimit : double.MaxValue;

        string timeUnitStr = cfg.TimeUnit switch
        {
            TimeUnitType.Seconds => "sec",
            TimeUnitType.Hours => "hr",
            _ => "min"
        };

        // Server states: when each server is free next
        double[] serverFreeAt = new double[numServers];
        double[] serverBusyDuration = new double[numServers];

        var customerRows = new List<CustomerEventRow>(Math.Min(maxCustomers, 100_000));

        // Build distribution instances once — reused across all events
        IDistribution? arrDistInst = BuildDistInstance(cfg.ArrivalDist);
        IDistribution? svcDistInst = BuildDistInstance(cfg.ServiceDist);

        // Service-start times of vehicles still waiting in the queue
        var waiting = new PriorityQueue<double, double>();
        // Q(t) changes: +1 when a vehicle joins the queue, −1 when its service begins
        var queueChanges = new List<(double Time, int Delta)>();

        double currentArrival = 0;
        bool stoppedByDurationCap = false;

        for (int i = 1; i <= maxCustomers; i++)
        {
            double rArr = Math.Clamp(Math.Round(rng.NextDouble(), 4), MinR, MaxR);
            double iat = SampleDist(cfg.ArrivalDist, rArr, arrDistInst);
            currentArrival += iat;

            if (currentArrival > maxDuration)
            {
                stoppedByDurationCap = true;
                break;
            }

            double rSvc = Math.Clamp(Math.Round(rng.NextDouble(), 4), MinR, MaxR);
            double svc = SampleDist(cfg.ServiceDist, rSvc, svcDistInst);

            // Find earliest available server
            int bestServer = 0;
            double earliestFree = serverFreeAt[0];
            for (int s = 1; s < numServers; s++)
            {
                if (serverFreeAt[s] < earliestFree)
                {
                    earliestFree = serverFreeAt[s];
                    bestServer = s;
                }
            }

            double svcBegins = Math.Max(currentArrival, earliestFree);
            double waitInQueue = svcBegins - currentArrival;
            double svcEnds = svcBegins + svc;
            double timeInSystem = waitInQueue + svc;

            // Vehicles whose service has started by now have left the queue
            while (waiting.Count > 0 && waiting.Peek() <= currentArrival)
                waiting.Dequeue();
            int qLenAtArrival = waiting.Count;

            if (waitInQueue > 0)
            {
                waiting.Enqueue(svcBegins, svcBegins);
                queueChanges.Add((currentArrival, +1));
                queueChanges.Add((svcBegins, -1));
            }

            double idleTime = Math.Max(0, currentArrival - earliestFree);

            serverFreeAt[bestServer] = svcEnds;
            serverBusyDuration[bestServer] += svc;

            customerRows.Add(new CustomerEventRow
            {
                CustomerNumber = i,
                ArrivalRandom = rArr,
                InterarrivalTime = Math.Round(iat, 4),
                ArrivalTime = Math.Round(currentArrival, 4),
                ServiceRandom = rSvc,
                ServiceTime = Math.Round(svc, 4),
                TimeServiceBegins = Math.Round(svcBegins, 4),
                TimeServiceEnds = Math.Round(svcEnds, 4),
                WaitingTime = Math.Round(waitInQueue, 4),
                TimeInSystem = Math.Round(timeInSystem, 4),
                QueueLengthOnArrival = qLenAtArrival,
                AssignedServer = bestServer + 1,
                IdleTimeOfServer = Math.Round(idleTime, 4)
            });
        }

        int totalServed = customerRows.Count;
        double totalSimTime = totalServed > 0 ? customerRows.Max(c => c.TimeServiceEnds) : 1.0;
        if (totalSimTime <= 0) totalSimTime = 1.0;

        // Q(t) as a time-ordered step function; area under it gives the time-average Lq
        var (timeline, queueArea) = BuildQueueTimeline(queueChanges);
        int maxQLen = timeline.Max(t => t.QLen);

        double avgIat = totalServed > 0 ? customerRows.Average(c => c.InterarrivalTime) : 0;
        double avgSvc = totalServed > 0 ? customerRows.Average(c => c.ServiceTime) : 0;
        double avgWq  = totalServed > 0 ? customerRows.Average(c => c.WaitingTime) : 0;
        double avgW   = totalServed > 0 ? customerRows.Average(c => c.TimeInSystem) : 0;
        double maxWq  = totalServed > 0 ? customerRows.Max(c => c.WaitingTime) : 0;

        // Measured time averages: Lq from the area under Q(t); L adds the vehicles in service
        double totalBusy = serverBusyDuration.Sum();
        double simLq = queueArea / totalSimTime;
        double simL  = (queueArea + totalBusy) / totalSimTime;

        double avgUtil = totalBusy / (numServers * totalSimTime);
        if (avgUtil > 1.0) avgUtil = 1.0;

        double throughput = totalSimTime > 0 ? totalServed / totalSimTime : 0;

        // Hourly breakdown
        var hourlyRows = GenerateHourlySummary(customerRows, numServers, timeUnitStr);

        // Theoretical Comparison
        var compRows = GenerateTheoreticalComparison(cfg, avgIat, avgSvc, avgWq, avgW, simLq, simL, avgUtil, timeUnitStr);

        // Waiting Time Distribution
        var waitDist = GenerateWaitDistribution(customerRows);

        return new DESSimulationResult(
            CustomerRows: customerRows,
            HourlyRows: hourlyRows,
            ComparisonRows: compRows,
            QueueTimeline: timeline,
            WaitDistribution: waitDist,
            AvgInterarrivalTime: avgIat,
            AvgServiceTime: avgSvc,
            AvgWaitingTime: avgWq,
            AvgSystemTime: avgW,
            AvgQueueLength: simLq,
            AvgSystemLength: simL,
            MaxQueueLength: maxQLen,
            MaxWaitingTime: maxWq,
            ServerUtilization: avgUtil,
            Throughput: throughput,
            TotalCustomersServed: totalServed,
            TotalSimTime: totalSimTime,
            TimeUnitStr: timeUnitStr,
            StoppedByDurationCap: stoppedByDurationCap,
            CustomerLimit: maxCustomers
        );
    }

    /// <summary>
    /// Sorts the +1/−1 queue changes by time and expands them into step-function points
    /// (two points per change, so the line chart draws vertical steps).
    /// </summary>
    private static (List<(double Time, int QLen)> Timeline, double Area) BuildQueueTimeline(
        List<(double Time, int Delta)> changes)
    {
        // At equal times, process service starts (−1) before arrivals (+1)
        changes.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Delta.CompareTo(b.Delta));

        var timeline = new List<(double Time, int QLen)>(changes.Count * 2 + 1) { (0, 0) };
        int q = 0;
        double lastT = 0, area = 0;
        foreach (var (t, delta) in changes)
        {
            area += q * (t - lastT);
            lastT = t;
            timeline.Add((t, q));
            q += delta;
            timeline.Add((t, q));
        }
        return (timeline, area);
    }

    private static double SampleDist(DistConfig dist, double u, IDistribution? distInstance)
    {
        // Every distribution uses inverse transform on u, so the displayed R produced the value.
        switch (dist.Type)
        {
            case DistType.Exponential:
                double mean = dist.Param1 > 0 ? dist.Param1 : 1.0;
                return -mean * Math.Log(u);

            case DistType.Uniform:
                double min = dist.Param1;
                double max = dist.Param2 > min ? dist.Param2 : min + 1.0;
                return min + u * (max - min);

            case DistType.Normal:
                return ((NormalDistribution)distInstance!).Quantile(u);

            case DistType.Gamma:
                return ((GammaDistribution)distInstance!).Quantile(u);

            default:
                return 1.0;
        }
    }

    private static IDistribution? BuildDistInstance(DistConfig dist)
    {
        return dist.Type switch
        {
            DistType.Normal => new NormalDistribution(
                dist.Param1,
                dist.Param2 > 0 ? dist.Param2 : 1.0),
            DistType.Gamma => new GammaDistribution(
                dist.Param1 > 0 ? dist.Param1 : 2.0,
                dist.Param2 > 0 ? dist.Param2 : 1.0),
            _ => null
        };
    }

    /// <summary>
    /// Theoretical mean and variance of a configured distribution, using the same
    /// parameter fallbacks as the samplers. Normal moments are untruncated, so they
    /// drift from the simulation when σ is large relative to the mean.
    /// </summary>
    public static (double Mean, double Variance) Moments(DistConfig dist)
    {
        switch (dist.Type)
        {
            case DistType.Exponential:
            {
                double mean = dist.Param1 > 0 ? dist.Param1 : 1.0;
                return (mean, mean * mean);
            }
            case DistType.Uniform:
            {
                double a = dist.Param1;
                double b = dist.Param2 > a ? dist.Param2 : a + 1.0;
                return ((a + b) / 2.0, (b - a) * (b - a) / 12.0);
            }
            case DistType.Gamma:
            {
                double k = dist.Param1 > 0 ? dist.Param1 : 2.0;
                double theta = dist.Param2 > 0 ? dist.Param2 : 1.0;
                return (k * theta, k * theta * theta);
            }
            default: // Normal
            {
                double std = dist.Param2 > 0 ? dist.Param2 : 1.0;
                return (dist.Param1, std * std);
            }
        }
    }

    private static List<HourlySummaryRow> GenerateHourlySummary(
        List<CustomerEventRow> rows, int servers, string unit)
    {
        if (rows.Count == 0) return [];

        double maxTime = rows.Max(r => r.TimeServiceEnds);
        double interval = unit == "hr" ? 1.0 : unit == "sec" ? 3600.0 : 60.0;
        int totalPeriods = Math.Max(1, (int)Math.Ceiling(maxTime / interval));

        // Pre-allocate per-period accumulators for a single O(n) pass
        var arrCount  = new int[totalPeriods];
        var depCount  = new int[totalPeriods];
        var waitSum   = new double[totalPeriods];
        var qSum      = new double[totalPeriods];
        var peakQ     = new double[totalPeriods];
        var busySum   = new double[totalPeriods];

        foreach (var r in rows)
        {
            int arrBin = Math.Min((int)(r.ArrivalTime / interval), totalPeriods - 1);
            int depBin = Math.Min((int)(r.TimeServiceEnds / interval), totalPeriods - 1);

            arrCount[arrBin]++;
            qSum[arrBin] += r.QueueLengthOnArrival;
            if (r.QueueLengthOnArrival > peakQ[arrBin])
                peakQ[arrBin] = r.QueueLengthOnArrival;

            depCount[depBin]++;
            waitSum[depBin] += r.WaitingTime;

            // Clip service overlap to each period
            for (int p = Math.Min(arrBin, depBin); p <= depBin && p < totalPeriods; p++)
            {
                double tStart = p * interval;
                double tEnd   = (p + 1) * interval;
                busySum[p] += Math.Max(0.0, Math.Min(r.TimeServiceEnds, tEnd)
                                          - Math.Max(r.TimeServiceBegins, tStart));
            }
        }

        var list = new List<HourlySummaryRow>(totalPeriods);
        for (int h = 0; h < totalPeriods; h++)
        {
            double tStart = h * interval;
            double tEnd   = (h + 1) * interval;
            double avgW   = depCount[h] > 0 ? waitSum[h] / depCount[h] : 0;
            double avgQ   = arrCount[h] > 0 ? qSum[h]  / arrCount[h]  : 0;
            double util   = Math.Min(1.0, busySum[h] / (servers * interval));

            list.Add(new HourlySummaryRow
            {
                HourPeriod        = $"Period {h + 1} ({tStart:F0}–{tEnd:F0} {unit})",
                Arrivals          = arrCount[h],
                Departures        = depCount[h],
                AvgWait           = Math.Round(avgW, 2),
                AvgQueueLength    = Math.Round(avgQ, 2),
                PeakQueueLength   = (int)peakQ[h],
                ServerUtilization = Math.Round(util * 100.0, 1)
            });
        }
        return list;
    }

    private static List<TheoreticalComparisonRow> GenerateTheoreticalComparison(
        DESSimConfig cfg, double avgIat, double avgSvc, double simWq, double simW,
        double simLq, double simL, double simRho, string unit)
    {
        var list = new List<TheoreticalComparisonRow>();

        var (arrMean, arrVar) = Moments(cfg.ArrivalDist);
        var (svcMean, svcVar) = Moments(cfg.ServiceDist);
        double theoLambda = arrMean > 0 ? 1.0 / arrMean : 1.0;
        double theoMu     = svcMean > 0 ? 1.0 / svcMean : 1.0;

        // Calculate theoretical model
        QueueMetrics? theo;
        if (cfg.NumServers == 1 && cfg.ArrivalDist.Type == DistType.Exponential && cfg.ServiceDist.Type == DistType.Exponential)
            (theo, _) = MM1Calculator.Calculate(theoLambda, theoMu);
        else if (cfg.NumServers > 1 && cfg.ArrivalDist.Type == DistType.Exponential && cfg.ServiceDist.Type == DistType.Exponential)
            (theo, _) = MMsCalculator.Calculate(theoLambda, theoMu, cfg.NumServers);
        else if (cfg.NumServers == 1 && cfg.ArrivalDist.Type == DistType.Exponential)
            (theo, _) = MG1Calculator.Calculate(theoLambda, theoMu, svcVar);
        else if (cfg.NumServers > 1 && cfg.ArrivalDist.Type == DistType.Exponential)
            (theo, _) = MGsCalculator.Calculate(theoLambda, theoMu, cfg.NumServers, svcVar);
        else if (cfg.NumServers == 1)
            (theo, _) = GG1Calculator.Calculate(theoLambda, theoMu, arrVar, svcVar);
        else
            (theo, _) = GGsCalculator.Calculate(theoLambda, theoMu, cfg.NumServers, arrVar, svcVar);

        double simLambda = avgIat > 0 ? 1.0 / avgIat : 0;
        double simMu = avgSvc > 0 ? 1.0 / avgSvc : 0;

        AddComp(list, "Arrival Rate", "λ", $"{simLambda:F4} /{unit}", $"{theoLambda:F4} /{unit}", simLambda, theoLambda);
        AddComp(list, "Service Rate", "μ", $"{simMu:F4} /{unit}", $"{theoMu:F4} /{unit}", simMu, theoMu);

        if (theo != null)
        {
            AddComp(list, "Server Utilization", "ρ", $"{simRho * 100:F1}%", $"{theo.Rho * 100:F1}%", simRho, theo.Rho);
            AddComp(list, "Avg Waiting Time in Queue", "Wq", $"{simWq:F4} {unit}", $"{theo.Wq:F4} {unit}", simWq, theo.Wq);
            AddComp(list, "Avg Time in System", "W", $"{simW:F4} {unit}", $"{theo.W:F4} {unit}", simW, theo.W);
            AddComp(list, "Avg Queue Length (time avg)", "Lq", $"{simLq:F4} veh", $"{theo.Lq:F4} veh", simLq, theo.Lq);
            AddComp(list, "Avg System Length (time avg)", "L", $"{simL:F4} veh", $"{theo.L:F4} veh", simL, theo.L);
        }

        // Little's Law check: measured time-average L against λ·W from the per-vehicle times
        double littleL = simLambda * simW;
        AddComp(list, "Little's Law check (L vs λ·W)", "L = λW", $"{simL:F4} veh", $"{littleL:F4} veh (λ·W)", simL, littleL);

        return list;
    }

    private static void AddComp(List<TheoreticalComparisonRow> list, string name, string sym, string simStr, string theoStr, double simVal, double theoVal)
    {
        double diff = theoVal != 0 ? Math.Abs(simVal - theoVal) / theoVal * 100.0 : 0;
        list.Add(new TheoreticalComparisonRow
        {
            MetricName = name,
            Symbol = sym,
            SimulatedValue = simStr,
            TheoreticalValue = theoStr,
            ErrorDelta = $"{diff:F2}%"
        });
    }

    private static List<(double WaitBin, int Count, double Pct)> GenerateWaitDistribution(List<CustomerEventRow> rows)
    {
        var list = new List<(double WaitBin, int Count, double Pct)>();
        if (rows.Count == 0) return list;

        double maxW = rows.Max(r => r.WaitingTime);
        int binCount = 6;
        double binWidth = maxW > 0 ? maxW / binCount : 1.0;

        // Single pass: bin index = floor(w / width), with the maximum folded into the last bin
        var counts = new int[binCount];
        foreach (var r in rows)
            counts[Math.Min((int)(r.WaitingTime / binWidth), binCount - 1)]++;

        for (int b = 0; b < binCount; b++)
        {
            double hi = (b + 1) * binWidth;
            double pct = (double)counts[b] / rows.Count * 100.0;
            list.Add((Math.Round(hi, 2), counts[b], Math.Round(pct, 1)));
        }

        return list;
    }
}
