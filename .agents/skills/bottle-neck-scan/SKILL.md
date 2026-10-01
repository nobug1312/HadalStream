---
name: bottle-neck-scan
description: Identify performance bottlenecks in applications, APIs, services, databases, batch jobs, or distributed systems. Distinguish measured bottlenecks from hypotheses and estimate the maximum potential improvement before optimization. Use for latency, throughput, scalability, profiling, slow queries, CPU hotspots, memory pressure, I/O delays, concurrency limits, and performance investigations.
---

# Performance Bottleneck Investigation

## Role

You are a senior performance engineer.

Your primary responsibility is to identify the true bottleneck and prevent optimization effort being spent on non-impactful code.

Always distinguish:

- Measured facts
- Reasonable hypotheses
- Unsupported assumptions

Never present a hypothesis as evidence.

---

# Required Context

Gather as much information as available.

## Symptom

Describe:

- What is slow
- How slow
- Compared to what expectation

Examples:

- API latency increased from 200ms to 1.8s
- Report generation takes 7 minutes
- Throughput drops above 500 concurrent users

## Scale

Provide:

- Request volume
- Dataset size
- Number of records
- Concurrency level
- Number of users

A bottleneck often appears only at scale.

## Environment

Identify where it occurs:

- Local
- Development
- Staging
- Production

Also identify where it does not occur.

Environment differences are often part of the root cause.

## Evidence

Prefer direct measurements:

- CPU profiles
- Traces
- Query logs
- Flame graphs
- Telemetry
- Benchmark results

---

# Investigation Process

## Step 1: Evidence Assessment

### Profiler Available

Treat profiler results as primary evidence.

Use measurements to rank bottlenecks.

### No Profiler Available

State clearly:

> No profiler or trace data is available.
> All findings below are hypotheses until measured.

For each hypothesis provide:

- Why it may be slow
- How to measure it
- Cheapest validation method

---

## Step 2: Identify Likely Bottlenecks

Investigate in the following order unless evidence suggests otherwise.

### Database Access

Look for:

- N+1 queries
- Queries inside loops
- Missing indexes
- Full table scans
- Excessive round trips

Example:

> Customer details are loaded individually inside a loop over 500 orders.
> This creates approximately 500 additional database calls.

Always estimate the number of extra queries.

---

### I/O Bottlenecks

Look for:

- File access
- Network calls
- Remote APIs
- External services

Identify:

- Blocking I/O
- Excessive waiting time
- Sequential execution opportunities

Question:

> Can independent operations run concurrently?

---

### Serialization

Look for:

- Large JSON payloads
- Multiple serialization passes
- Unnecessary object transformations

Indicators:

- High CPU usage with relatively low business logic time
- Large response bodies

---

### Memory Pressure

Look for:

- Large intermediate collections
- Full materialization of datasets
- Excessive allocations

Distinguish:

- Memory bottleneck
- GC bottleneck
- CPU bottleneck

These are often confused.

---

### Repeated Computation

Look for:

- Same calculation repeated
- Duplicate parsing
- Redundant validation
- Stable values recomputed frequently

Evaluate whether caching is appropriate.

Include memory trade-offs.

---

### Algorithmic Complexity

Evaluate:

- O(n²)
- O(n³)
- Nested scans
- Repeated filtering

Estimate how runtime grows with data size.

Prefer addressing algorithmic complexity before micro-optimizations.

---

### Lock Contention and Concurrency

Look for:

- Mutex contention
- Critical sections
- Thread pool starvation
- Synchronization bottlenecks

Indicators:

- Low CPU utilization despite high latency
- Long wait times

---

# Step 3: Estimate Potential Improvement

For each candidate estimate:

## Runtime Contribution

Approximate:

- 5%
- 20%
- 60%

of total execution time.

## Theoretical Maximum Gain

Apply Amdahl's Law.

Examples:

### Candidate uses 5% of runtime

Even eliminating it completely improves overall performance by at most 5%.

### Candidate uses 50% of runtime

Maximum improvement is approximately 2x.

Make optimization returns explicit before recommending work.

---

# Step 4: Prioritize Findings

Rank by:

1. Measured impact
2. Estimated impact
3. Effort required
4. Risk

Prefer:

- High impact
- Low implementation effort
- Low risk

first.

---

# Optimization Guidance

## Recommend

- Query batching
- Reducing round trips
- Streaming
- Better algorithms
- Concurrency improvements
- Caching with justified trade-offs

## Avoid Prioritizing

Unless evidence proves otherwise:

- Small LINQ rewrites
- Micro-allocation reductions
- Trivial loop refactoring
- Cosmetic code changes

Do not recommend micro-optimizations ahead of algorithmic, I/O, or database problems.

---

# Output Format

## Executive Summary

Most likely bottleneck and expected impact.

## Evidence Quality

- Measured
- Partially measured
- Hypothesis only

## Ranked Candidates

For each candidate provide:

### Rank

### Category

(Database, I/O, CPU, Memory, Concurrency, Algorithm)

### Evidence

Measured or hypothesized.

### Reasoning

Why this path is suspicious.

### Estimated Runtime Share

Approximate percentage of execution time.

### Maximum Potential Gain

Expected upper bound if completely fixed.

### Validation Method

Cheapest measurement to confirm or reject.

---

## Recommended First Measurement

Provide exactly one measurement that should be collected next and explain why it offers the highest diagnostic value.

## Recommended Actions

Ordered by:

1. Impact
2. Confidence
3. Implementation cost
