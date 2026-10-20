using System;
using System.Collections.Generic;
using System.Linq;
using FuelQ.Distributions;

namespace FuelQ.Simulation;

/// <summary>Result returned by ForecourtSimulator.Run()</summary>
/// <remarks>
/// Averages cover vehicles whose service finished within the run; vehicles still
/// waiting or fueling at the end are excluded, so waits are slightly understated.
/// </remarks>
public record SimulationResult(
    List<SimulationLogRecord>          Logs,
    List<(double Time, int QLen)>      QueueTimeline,
    double AverageWaitingTime,
    double AverageSystemTime,
    int    MaxQueueLength,
    int    VehiclesProcessed,
    double[] ServerUtilization,
    double AverageQueueLength);   // time-weighted mean of Q(t) over the run

/// <summary>
/// Event-list discrete-event simulation engine (events kept in a sorted set).
/// Models the Shell Maskan Chowrangi forecourt with parallel pumps,
/// 70/30 bike-car mix, Branch 4B (Cash) vs Branch 4A-5 (POS/receipt).
/// </summary>
public static class ForecourtSimulator
{
    private enum EventKind { Arrival, ServiceEnd }

    private record SimEvent(double Time, EventKind Kind, int VehicleId, int PumpId) : IComparable<SimEvent>
    {
        public int CompareTo(SimEvent? other)
        {
            if (other is null) return 1;
            int c = Time.CompareTo(other.Time);
            return c != 0 ? c : Kind.CompareTo(other.Kind);
        }
    }

    public static SimulationResult Run(ForecourtConfig cfg)
    {
        var rng      = new Random(cfg.RandomSeed);
        var arrDist  = new ExponentialDistribution(cfg.ArrivalRate);
        var bikeDist = new ExponentialDistribution(cfg.BikeServiceRate);
        var carDist  = new ExponentialDistribution(cfg.CarServiceRate);

        // Per-pump state: queue of waiting vehicles + current finish time
        double[]            pumpFreeAt  = new double[cfg.NumServers];
        double[]            pumpBusySec = new double[cfg.NumServers];
        Queue<VehicleEntity>[] pumpQueues  = new Queue<VehicleEntity>[cfg.NumServers];
        for (int i = 0; i < cfg.NumServers; i++) pumpQueues[i] = new Queue<VehicleEntity>();

        var logs          = new List<SimulationLogRecord>();
        var qTimeline     = new List<(double Time, int QLen)>();
        var vehicles      = new List<VehicleEntity>();
        var vehiclesById  = new Dictionary<int, VehicleEntity>();

        var events = new SortedSet<SimEvent>(Comparer<SimEvent>.Create((a, b) =>
        {
            int c = a.Time.CompareTo(b.Time);
            if (c != 0) return c;
            c = a.Kind.CompareTo(b.Kind);
            return c != 0 ? c : a.VehicleId.CompareTo(b.VehicleId);
        }));

        double clock        = 0;
        int    vehicleCount = 0;
        int    totalQueue   = 0;
        int    queueNow     = 0;

        // Schedule first arrival
        double nextArrival = arrDist.Sample(rng);
        events.Add(new SimEvent(nextArrival, EventKind.Arrival, ++vehicleCount, -1));

        while (events.Count > 0 && clock <= cfg.SimDurationSeconds)
        {
            var ev = events.Min!;
            events.Remove(ev);
            clock = ev.Time;

            if (clock > cfg.SimDurationSeconds) break;

            if (ev.Kind == EventKind.Arrival)
            {
                // --- Create vehicle ---
                bool isBike = rng.NextDouble() < cfg.BikeFraction;
                string vtype = isBike ? "Bike" : "Car";

                string payment;
                if (isBike)       payment = "Cash";
                else { double r = rng.NextDouble(); payment = r < 0.50 ? "Cash" : r < 0.78 ? "Card_POS" : "QR_Pay"; }

                // All cash payers (bikes and cars) use the fast 4B lane; card/QR payers use 4A-POS
                string branch = payment == "Cash" ? "4B-Cash" : "4A-POS";

                double svcDuration = isBike ? bikeDist.Sample(rng) : carDist.Sample(rng);
                // Branch 4A-5 adds handover + receipt time
                if (branch == "4A-POS") svcDuration += 10.0 + rng.NextDouble() * 15.0;
                // Occasional calibration delay
                if (rng.NextDouble() < 0.04) svcDuration += 8.0 + rng.NextDouble() * 12.0;
                svcDuration = Math.Max(5, svcDuration);

                var vehicle = new VehicleEntity
                {
                    Id             = ev.VehicleId,
                    VehicleType    = vtype,
                    PaymentBranch  = branch,
                    PaymentMethod  = payment,
                    ArrivalTime    = clock,
                    ServiceDuration= svcDuration
                };

                // Find shortest-queue pump
                int bestPump = 0;
                for (int p = 1; p < cfg.NumServers; p++)
                    if (pumpQueues[p].Count < pumpQueues[bestPump].Count ||
                       (pumpQueues[p].Count == pumpQueues[bestPump].Count && pumpFreeAt[p] < pumpFreeAt[bestPump]))
                        bestPump = p;

                queueNow++;
                totalQueue += queueNow;

                logs.Add(MakeLog(clock, "Arrival", vehicle, -1, queueNow, branch, ""));
                qTimeline.Add((clock, queueNow));
                vehicles.Add(vehicle);
                vehiclesById[vehicle.Id] = vehicle;

                if (pumpFreeAt[bestPump] <= clock)
                {
                    // Pump is free — serve immediately
                    double finish = clock + svcDuration;
                    pumpFreeAt[bestPump]  = finish;
                    pumpBusySec[bestPump]+= BusyWithinRun(clock, finish, cfg.SimDurationSeconds);
                    vehicle.AssignedPump  = bestPump + 1;
                    vehicle.ServiceStart  = clock;
                    vehicle.ServiceEnd    = finish;
                    queueNow--;
                    logs.Add(MakeLog(clock, "ServiceStart", vehicle, bestPump + 1, queueNow, branch, "Pump free"));
                    qTimeline.Add((clock, queueNow));
                    events.Add(new SimEvent(finish, EventKind.ServiceEnd, vehicle.Id, bestPump));
                }
                else
                {
                    // Queue at best pump. Estimated wait = rest of the current service
                    // plus the service times of every vehicle already queued at this pump.
                    int ahead = pumpQueues[bestPump].Count;
                    double estWait = pumpFreeAt[bestPump] - clock + pumpQueues[bestPump].Sum(v => v.ServiceDuration);
                    pumpQueues[bestPump].Enqueue(vehicle);
                    logs.Add(MakeLog(clock, "Queued", vehicle, bestPump + 1, queueNow, branch,
                        $"Wait ≈ {estWait:F0}s ({ahead} ahead)"));
                }

                // Schedule next arrival
                nextArrival = clock + arrDist.Sample(rng);
                if (nextArrival <= cfg.SimDurationSeconds)
                    events.Add(new SimEvent(nextArrival, EventKind.Arrival, ++vehicleCount, -1));
            }
            else // ServiceEnd
            {
                int pump = ev.PumpId;
                queueNow = Math.Max(0, queueNow);
                logs.Add(MakeLog(clock, "ServiceEnd", vehiclesById.GetValueOrDefault(ev.VehicleId), pump + 1, queueNow, "", "Departed"));
                qTimeline.Add((clock, queueNow));

                // Serve next in this pump's queue
                if (pumpQueues[pump].Count > 0)
                {
                    var next = pumpQueues[pump].Dequeue();
                    double finish = clock + next.ServiceDuration;
                    pumpFreeAt[pump]  = finish;
                    pumpBusySec[pump]+= BusyWithinRun(clock, finish, cfg.SimDurationSeconds);
                    next.AssignedPump  = pump + 1;
                    next.ServiceStart  = clock;
                    next.ServiceEnd    = finish;
                    queueNow--;
                    logs.Add(MakeLog(clock, "ServiceStart", next, pump + 1, Math.Max(0, queueNow), next.PaymentBranch, "Next in queue"));
                    qTimeline.Add((clock, Math.Max(0, queueNow)));
                    events.Add(new SimEvent(finish, EventKind.ServiceEnd, next.Id, pump));
                }
                else
                {
                    pumpFreeAt[pump] = clock; // pump now idle
                }
            }
        }

        // Compute summary statistics
        var served = vehicles.Where(v => v.ServiceEnd > 0).ToList();
        double avgWq = served.Count > 0 ? served.Average(v => v.WaitingTime) : 0;
        double avgW  = served.Count > 0 ? served.Average(v => v.SystemTime)  : 0;
        int maxQ     = qTimeline.Count > 0 ? qTimeline.Max(t => t.QLen) : 0;

        double[] utilization = new double[cfg.NumServers];
        double simDuration = Math.Min(clock, cfg.SimDurationSeconds);
        for (int p = 0; p < cfg.NumServers; p++)
            utilization[p] = simDuration > 0 ? Math.Min(1.0, pumpBusySec[p] / simDuration) : 0;

        // Time-weighted Lq: each queue length holds until the next timeline point (or the end of the run)
        double area = 0;
        for (int i = 0; i < qTimeline.Count; i++)
        {
            double tEnd = i + 1 < qTimeline.Count ? qTimeline[i + 1].Time : simDuration;
            area += qTimeline[i].QLen * Math.Max(0, tEnd - qTimeline[i].Time);
        }
        double avgLq = simDuration > 0 ? area / simDuration : 0;

        return new SimulationResult(logs, qTimeline, avgWq, avgW, maxQ, served.Count, utilization, avgLq);
    }

    /// <summary>Busy time of a service in [start, finish], clipped to the end of the run.</summary>
    private static double BusyWithinRun(double start, double finish, double runEnd) =>
        Math.Max(0, Math.Min(finish, runEnd) - start);

    private static SimulationLogRecord MakeLog(double clock, string evType,
        VehicleEntity? v, int pump, int qlen, string path, string notes)
    {
        return new SimulationLogRecord
        {
            ClockTime   = TimeSpan.FromSeconds(clock).ToString(@"hh\:mm\:ss"),
            EventType   = evType,
            VehicleId   = v != null ? $"V-{v.Id:D3}" : "—",
            VehicleType = v?.VehicleType ?? "—",
            PumpId      = pump > 0 ? $"Pump #{pump}" : "—",
            QueueLength = qlen,
            PaymentPath = path,
            Notes       = notes
        };
    }
}
