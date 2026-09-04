Not gating (no .expect.json - the harness skips it). #191 step 10 finding, 2026-09-04: under
HandWeightedEvaluator's 55%-objective/30%-value/15%-threat split, a "stand and shoot with a
clearly superior gun" choice loses value-share ground to any action that also happens to close on
a distant, unclaimed objective - Charge (which ends closer to the objectives at [36,44] etc.) beat
Shoot even against a nearly worthless target, and with objectives placed out of reach the whole
leaf signal went flat (~0.50-0.50 across every root edge) and the choice became close to
prior-driven noise. The one-ply TacticianPlanner.Score ranks Shoot correctly in every geometry
tried; only the multi-ply search disagrees. Left as a scenario for the B-gate failure analysis
(Opus) rather than tuned until it happens to pass - see the 2026-09-04 ledger entry.
