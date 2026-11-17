# Listing replay

Replay archived OLX rows, saved SCFD OLX envelopes and retained Joymee detail responses through the current listing mapper. Joymee passes through the production reader and rules extractor with AI off. This offline tool reads files and prints aggregate JSON.

```sh
dotnet run --project tools/ListingReplay -- \
  --olx-archive "$listing_archive_directory" \
  --olx-local "$local_olx_export" \
  --joymee "$joymee_export" > listing-replay.json
```

Generate local exports from the authorized capture store. Use the Joymee request URL accepted by the reader for the detail document. Omit source URL and transaction for archive rows whose saved scope is unknown.

| Argument | Input |
| --- | --- |
| `--olx-archive` | Directory containing the historical Haven `batch*.json` arrays. Each row supplies `title`, `description`, `price`, `posted_at`, `subregion`, `is_negotiable` and the `parameters` array where available. |
| `--olx-local` | JSON array of objects containing `url` and `envelope`. The envelope uses the SCFD post wire contract. |
| `--joymee` | JSON array of objects containing `url` and `body`, with an optional RFC 3339 `capturedAtUtc`. Body may be the parsed detail object or its original JSON text. |

Each metric counts observations with the reported field, code, or flag. Array indices are grouped to compare observations with different numbers of offers. Native presence is measured after extraction. Observations distinguish repeated captures from the number of distinct marketplace ads. The report includes bounded numeric enum codes classified as unmapped by the mapper. Keep export files in private temporary storage and remove them after replay.

Exit 0 means that every supplied observation produced the listing projection and all three cohorts contained data. Exit 1 includes the aggregate result and the reason any projection failed. Exit 2 signals invalid input.
