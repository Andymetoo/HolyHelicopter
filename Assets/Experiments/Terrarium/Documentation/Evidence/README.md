# Evidence contents

The main `metrics.csv`, `action-counts.csv`, `run-report.md`, `validation-status.txt`, and `source-manifest.txt` come from the final eight-seed, four-stress-condition harness run. The `seed19-` versions of those files describe the supplementary 2,000-tick trace using the same executable. Source manifests include SHA-256 hashes and exact arguments. The source hashes were checked again after validation.

The compact event/decision/social-evaluation CSVs are exact selected rows from the final traces, with their original seed, tick, agent and event IDs. Each selected event file also contains the recursively linked event ancestors of the examples. Decision rows include the chosen reason and the retained alternatives. They are curated illustrations, not a random or exhaustive sample of the runs.

Raw trace files are intentionally kept outside Unity's imported asset tree:

* Main run: `Temp/Terrarium/evidence/trace-events.csv`, `trace-decisions.csv`, `trace-relations.csv`, `trace-relation-changes.csv`.
* Supplementary run: the same filenames in `Temp/Terrarium/evidence-seed19/`.

Paths above are relative to the project root. These temporary files can be regenerated using the manifests and the documented harness. Long decision traces are tens of megabytes; the excerpts here preserve the report's concrete claims without making Unity import those large trace files.

`static-validation.txt` records the separate compile check against installed Unity reference assemblies. It is not a Unity runtime or visual test. The project's requested editor version remains a manual import check.

See [the assessment](../ExperimentReport.md) for interpretation, limitations, and the precise scope of each causal claim. A causal link documents an actual recorded dependency or information source; it does not establish that its parent was the only necessary cause.
