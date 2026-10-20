using System.Collections.Generic;

namespace FuelQ.Controls;

/// <summary>
/// Plain-language explanations for every metric label in the app. Shown as a tooltip
/// when the user hovers a metric tile (see <see cref="Motion.AutoExplainProperty"/>).
/// </summary>
public static class Glossary
{
    private static readonly Dictionary<string, string> Entries = new()
    {
        ["ARRIVAL RATE (λ)"] = "λ: how many vehicles arrive per time unit, on average.\nExample: one vehicle every 3 min → λ = 1/3 = 0.333 per min.",
        ["SERVICE RATE (μ)"] = "μ: how many vehicles ONE pump can fuel per time unit.\nExample: 2 min per vehicle → μ = 1/2 = 0.5 per min.",
        ["TRAFFIC INTENSITY (a)"] = "a = λ / μ: how many pumps' worth of work arrives.\na = 0.67 means the arriving traffic would keep 0.67 pumps busy all the time.",
        ["UTILIZATION (ρ)"] = "ρ = λ / (s·μ): the share of time each pump is busy.\nBelow 100% the queue settles down; at 100% or more it grows forever.",
        ["SERVER UTILIZATION (ρ)"] = "The share of time the pumps were busy during the simulation\n(total fueling time ÷ (pumps × total time)).",
        ["IDLE PROBABILITY (P₀)"] = "P₀: the chance that the station is completely empty: no vehicle waiting or fueling.",
        ["WAIT PROBABILITY (Pw)"] = "Pw: the chance that an arriving vehicle finds every pump busy and has to wait.",
        ["AVG QUEUE LENGTH (Lq)"] = "Lq: the average number of vehicles WAITING in line (not counting those at a pump).",
        ["AVG SYSTEM LENGTH (L)"] = "L: the average number of vehicles at the station: waiting + fueling.",
        ["AVG WAIT IN QUEUE (Wq)"] = "Wq: the average time a vehicle waits in line before reaching a pump.",
        ["AVG WAITING TIME (Wq)"] = "Wq: the average time a vehicle waited in line before reaching a pump.",
        ["AVG TIME IN SYSTEM (W)"] = "W: the average total time at the station: waiting + fueling.\nW = Wq + 1/μ.",
        ["AVG SYSTEM TIME (W)"] = "W: the average total time a vehicle spent at the station: waiting + fueling.",
        ["MAX WAITING TIME"] = "The longest any single vehicle had to wait in this run.",
        ["MAX QUEUE LENGTH"] = "The longest the line ever got during this run (the highest point of the Q(t) chart).",
        ["SYSTEM THROUGHPUT"] = "How many vehicles were served per time unit over the whole run.",
        ["CUSTOMERS PROCESSED"] = "How many vehicles were simulated and fully served.",
        ["SIMULATION CLOCK"] = "Total simulated time, from the first arrival until the last vehicle left.",
        ["ARRIVAL (λ) veh/min"] = "Vehicles arriving per minute, on average.",
        ["SERVICE (μ) veh/min"] = "Vehicles one pump can fuel per minute, on average.",
        ["SERVER COUNT (C)"] = "Number of pumps working in parallel.",
        ["STD DEV (Σ) min"] = "How much fueling times vary (standard deviation, in minutes). Used by the M/G/1 model.",
        ["ERLANG SHAPE (K) phases"] = "k for the M/Eₖ/1 model: service split into k equal phases. Higher k = more regular service.",
        ["BINS (K)"] = "The arrival gaps are split into this many equal-probability groups for the χ² test.",
        ["DEG. FREEDOM (DF)"] = "Degrees of freedom = bins − 1 − 1 (one parameter, λ, was estimated from the data).",
        ["TEST STAT (χ²)"] = "χ² measures how far the observed counts are from what an exponential distribution predicts.\nSmaller = closer fit.",
        ["CRITICAL VALUE"] = "If χ² is below this value (at α = 0.05), the exponential assumption is not rejected.",
        ["P-VALUE"] = "The chance of seeing a fit this poor if arrivals really were exponential.\nAbove 0.05 → no evidence against the assumption.",
        ["INTER-ARRIVAL TIME (T)"] = "Time gaps between one vehicle arriving and the next.",
        ["SERVICE DURATION (S)"] = "Time each vehicle spent at the pump.",
    };

    /// <summary>Returns the explanation for a metric label, or null if there is none.</summary>
    public static string? Explain(string label) =>
        Entries.TryGetValue(label.Trim(), out var text) ? text : null;
}
