# FuelQ: Project Overview

FuelQ is a desktop app that models the vehicle queue at the Shell petrol pump at Maskan Chowrangi, Karachi. Cars and bikes arrive, wait for a free pump, get fuel and leave. The app answers questions like these:

- How long does a vehicle wait on average?
- How many vehicles are in the queue?
- How busy are the pumps?
- What changes if we add a pump?

It does this in two ways:

1. **Calculation:** queueing theory formulas give the long-run average answer.
2. **Simulation:** random vehicles are generated one by one, and the app records what happens to each of them.

The two results are then compared to check that they agree.

The app has **6 pages**, opened from the left sidebar.

---

## Key Terms

| Symbol | Meaning |
| :--- | :--- |
| λ (lambda) | Arrival rate: vehicles arriving per minute |
| μ (mu) | Service rate: vehicles one pump can fuel per minute |
| s | Number of pumps (servers) |
| a | Traffic load = λ / μ |
| ρ (rho) | Pump utilization: how busy the pumps are. ρ = λ / (s·μ) |
| Lq | Average number of vehicles waiting in the queue |
| L | Average number of vehicles in the system (waiting + fueling) |
| Wq | Average waiting time in the queue |
| W | Average total time at the station (waiting + fueling) |
| P₀ | Probability that the station is empty |
| Pw | Probability that an arriving vehicle has to wait |
| Ca, Cs | Variability of arrival times and service times (coefficient of variation = std dev / mean) |

**Stability rule:** the system is stable only if **ρ < 1**. If vehicles arrive faster than the pumps can serve them (ρ ≥ 1), the queue grows forever and the app shows a red **"System Unstable"** warning.

---

## Page 1: Home & Team

**Purpose:** introduction page. There are no calculations here.

- Project title, description and study site
- Supervisor (Dr. Shaista Rais) and the 7 members of Group G
- Site baseline used as the default inputs:
  - 2 to 4 pumps
  - Mean time between arrivals = 3 min (20 vehicles/hr)
  - Mean fueling time = 2 min per vehicle
- Shortcut cards that open the Calculator and the Simulator

---

## Page 2: Queueing Model Calculator

**Purpose:** compute the average performance of the pump station directly from formulas.

### Flow

1. **Pick a model:** M/M/1, M/M/s, M/G/1, M/G/s, G/G/1 or G/G/s.
   - The first letter is the arrival pattern, the second letter is the service pattern and the number is the number of pumps.
   - M = random (exponential), G = general (any distribution), 1 or s = one pump or several pumps.
2. **Enter inputs:**
   - Arrivals, as a mean time between vehicles or directly as a rate λ
   - Service, as a mean fueling time or directly as a rate μ
   - Number of pumps s (multi-pump models only)
   - The distribution (Exponential, Uniform or Normal), where the model allows a choice
   - For Uniform [a, b], the mean is taken from the bounds, (a + b) / 2, and the mean box becomes read-only
   - For Normal, the variance box is filled in automatically as σ²
3. **Derive rates:**
   - λ = 1 / mean interarrival time
   - μ = 1 / mean service time
   - a = λ / μ
   - ρ = λ / (s·μ)
4. **Check stability:** green badge if ρ < 1, red badge if ρ ≥ 1. When the system is unstable, the results show ∞. Invalid inputs show "Cannot compute" instead.
5. **Apply the model's formula** (see the table below) to get Lq, Wq, L, W, P₀ and Pw.
6. **Show the state probability table:** the chance of having exactly n vehicles at the station, for n = 0 to 10, plus the cumulative %. This table is shown only for M/M/1 and M/M/s, the only models where these formulas are exact.

The results update live whenever an input changes.

### Variance of the chosen distribution

The general models (G) need the spread of the times, not only their average.

| Distribution | Variance |
| :--- | :--- |
| Exponential | 1 / rate² |
| Uniform (min, max) | (max − min)² / 12 |
| Normal | std² |

### Formulas Used per Model

| Model | Name / Method | Main formula |
| :--- | :--- | :--- |
| **M/M/1** | Exact | Lq = ρ² / (1 − ρ), P₀ = 1 − ρ, Pw = ρ |
| **M/M/s** | Exact (Erlang-C) | P₀ = [ Σ(aⁿ/n!) for n=0..s−1 + aˢ / (s!·(1−ρ)) ]⁻¹ <br> Pw = aˢ / (s!·(1−ρ)) · P₀ <br> Lq = Pw · ρ / (1 − ρ) |
| **M/G/1** | Exact (Pollaczek–Khinchine) | Wq = λ · E[S²] / (2(1 − ρ)), where E[S²] = (1/μ)² + Var(S) |
| **M/G/s** | Approximation (Allen–Cunneen) | Wq ≈ [(1 + Cs²) / 2] × Wq of M/M/s |
| **G/G/1** | Approximation (Kingman) | Wq ≈ [(Ca² + Cs²) / 2] × [ρ / (1 − ρ)] × (1/μ) |
| **G/G/s** | Approximation (Allen–Cunneen) | Wq ≈ [(Ca² + Cs²) / 2] × Wq of M/M/s |

**Common follow-up formulas** (Little's Law), used by every model:

- Wq = Lq / λ, or Lq = λ · Wq
- W = Wq + 1/μ
- L = λ · W

**State probabilities P(n):**

- One pump: P(n) = (1 − ρ) · ρⁿ
- s pumps:
  - P(n) = P₀ · aⁿ / n! when n < s
  - P(n) = P₀ · (aˢ / s!) · ρⁿ⁻ˢ when n ≥ s

A **"Go to Simulator"** button opens Page 3.

---

## Page 3: Discrete Event Simulator (DES)

**Purpose:** simulate vehicles one at a time with random numbers, then compare the simulated results with the formula results.

### Inputs

- Number of pumps
- Time unit (seconds, minutes or hours)
- Maximum number of vehicles (default 20,000, so the results settle close to theory)
- Time limit (default 10,000,000). If the limit stops the run early, the page shows a warning.
- Random seed, so the same seed always gives the same run
- Arrival distribution: Exponential, Uniform or Normal
- Service distribution: Exponential, Normal, Gamma or Uniform

### Simulation Flow (for each vehicle)

1. **Draw a random number R** between 0 and 1.
2. **Convert it to an interarrival time** by inverse transform, time = F⁻¹(R), using the chosen distribution:
   - Exponential: time = −mean × ln(R)
   - Uniform: time = min + R × (max − min)
   - Normal: time = mean + σ · Φ⁻¹(R). Negative values are clipped to 0, so keep σ ≤ mean/3.
   - Gamma: the time t where the Gamma CDF equals R

   Because of this, every random number shown in the table really produced its time.
3. **Arrival time** = previous arrival time + interarrival time. The run stops if the time limit is passed.
4. **Draw another random number** and convert it to a service (fueling) time in the same way.
5. **Choose a pump:** the vehicle goes to the pump that becomes free earliest.
6. **Compute the vehicle's times:**
   - Service begins = max(arrival time, time the pump becomes free)
   - Waiting time = service begins − arrival time
   - Service ends = service begins + service time
   - Time in system = waiting time + service time
   - Pump idle time = max(0, arrival time − time the pump became free)
7. **Count the queue:** the queue seen by the arriving vehicle is the number of earlier vehicles still waiting to start service. A vehicle that has to wait adds +1 to Q(t) when it arrives and −1 when its service starts.
8. **Record everything** in the event table.

### Results Calculated After the Run

| Result | How it is calculated |
| :--- | :--- |
| Avg waiting time (Wq) | Average of all waiting times |
| Max waiting time | Largest waiting time |
| Avg time in system (W) | Average of all times in system |
| Simulated λ | 1 / average interarrival time |
| Avg queue length (Lq) | Area under the Q(t) curve / total time (time average) |
| Avg system length (L) | (Area under Q(t) + total pump busy time) / total time |
| Pump utilization (ρ) | Total busy time of all pumps / (pumps × total time) |
| Throughput | Vehicles served / total time |
| Max queue length | Highest point of the Q(t) curve |

### What the Page Shows

- **KPI cards** with the results listed above
- **Simulated vs Theoretical table.** The app picks the matching formula model automatically:
  - Exponential arrivals and exponential service: M/M/1 or M/M/s
  - Exponential arrivals and any other service: M/G/1 or M/G/s
  - Any other arrivals: G/G/1 or G/G/s

  Each row shows the error % = |simulated − theoretical| / theoretical × 100. A final row checks **Little's Law**: it compares the measured L with λ × W.
- **Random numbers table:** every random number drawn and the time it produced
- **Event-by-event table:** the full record of each vehicle (arrival, service start and end, waiting time, pump, idle time)
- **Hourly summary:** arrivals, departures, average wait, average and peak queue length, and pump utilization for each hour
- **Charts:**
  - Queue length over time, Q(t)
  - Waiting time histogram: % of vehicles in 6 equal waiting-time ranges

---

## Page 4: Empirical Data

**Purpose:** check whether arrivals really look random (exponential), which the M/… models assume.

### Flow

1. **Get data:** either generate the 320-vehicle **synthetic** baseline, or load a real field CSV.
   - The synthetic data is generated from Exp(λ = 2.8/min). It is not field data.
2. **Detect outliers** in service times with the IQR rule. A value is flagged if it is outside [Q1 − 1.5·IQR, Q3 + 1.5·IQR].
   - Interarrival times are not screened, because this rule wrongly flags normal values in skewed (exponential) data.
3. **Run the Chi-Square test** on all interarrival times:
   - Estimate λ = 1 / mean interarrival time.
   - Split the time axis into 10 bins, each with the same expected count n/10. The bin edges are −ln(1 − i/10) / λ.
   - Compute χ² = Σ (observed − expected)² / expected, with df = 10 − 1 − 1 = 8.
   - Compare χ² with the critical value (15.507 at α = 0.05). The p-value comes from the exact chi-square distribution.
   - If χ² < critical, H₀ ("arrivals are exponential") is not rejected.

On the synthetic baseline the test gives χ² = 14.69 and p = 0.066. This only demonstrates the method; it does not validate the real site.

---

## Page 5: Model Comparison

**Purpose:** put several models side by side for the same λ, μ (in vehicles per minute) and number of pumps.

- **Analytical rows:**
  - M/M/1
  - M/D/1, where Lq = ρ² / (2(1 − ρ))
  - M/G/1, with the variance taken as σ²
  - M/Eₖ/1, where Lq = [(1 + k) / 2k] · ρ² / (1 − ρ)
  - M/M/c
- **DES row:** one 1-hour run of the forecourt simulator.
  - 70% bikes and 30% cars.
  - Cash payers use the fast lane. Card and QR payers use the POS lane, which adds 10–25 s.
  - Lq is the time average of the queue.
- **Sensitivity curve:** Lq plotted against ρ from 0.10 to 0.98. It shows how the queue grows sharply as ρ approaches 1.
- **Efficiency gain:** the % drop in Lq when going from 1 pump to c pumps.
- **LaTeX export** of the table.

---

## Page 6: AI Prompts & Engineering Audit

**Purpose:** documentation and transparency. There are no calculations here.

- Lists the 5 phases of prompts used to build the app:
  1. UI shell
  2. Queue formulas
  3. DES engine
  4. Observation data and Chi-Square test
  5. Model comparison and LaTeX export
- Lists the theoretical assumptions behind the models and their limits. Every number quoted on this page is computed by the code, and the test suite checks that they match.

---

## Overall Logical Flow

```
Home page
   │
   ▼
Queue Calculator: inputs → λ, μ, ρ → stability check → formulas → Lq, Wq, L, W, P(n)
   │
   ▼
DES Simulator: random numbers → arrival and service times → pump assignment
              → waits per vehicle → Q(t) → averages → compare with the formula results
   │
   ▼
Empirical Data: dataset → outliers → χ² test of the exponential-arrival assumption
   │
   ▼
Model Comparison: all models + forecourt DES side by side → sensitivity curve
   │
   ▼
Audit page: how the app was built, and the assumptions and their limits
```
