# Uzexpocentre calendar

Reads the venue's English HTML exhibition calendar. Dates use the year printed immediately before the table; no date is inferred from the current year. Table rowspans carry organizer, date and pavilion values to each event.

Each row has a stable title/edition identity while retaining the real calendar URL as its source. Separate row origins prevent a shared calendar URL from merging different exhibitions. Calendar publication proves no visitor access, exhibitor availability or fee. HTML coverage remains a window; PDF-only entries need a separate reader.

Repeated titles in the same printed edition receive occurrence suffixes. The page
provides no native row IDs, so reordering otherwise indistinguishable repeated
titles cannot guarantee stable identity. Date changes and renumbering ordinary
rows do not change their keys.
