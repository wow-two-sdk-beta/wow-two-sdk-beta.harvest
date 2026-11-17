using System.Text.Json;

if (args is not ["--olx-archive", var archiveDirectory, "--olx-local", var localOlx, "--joymee", var joymee])
{
    Console.Error.WriteLine("Usage: ListingReplay --olx-archive <directory> --olx-local <file> --joymee <file>");
    return 2;
}

var archiveReport = new ReplayReport();
var localReport = new ReplayReport();
var joymeeReport = new ReplayReport();

try
{
    foreach (var envelope in ListingInputs.ArchivedOlx(archiveDirectory))
        archiveReport.Record(envelope, null);

    foreach (var (envelope, url) in ListingInputs.LocalOlx(localOlx))
        localReport.Record(envelope, url);

    await ListingInputs.ReplayJoymeeAsync(joymee, joymeeReport);
}
catch (ReplayInputException error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}

var outputOptions = new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true };
Console.WriteLine(JsonSerializer.Serialize(new { archiveOlx = archiveReport, localOlx = localReport, joymee = joymeeReport }, outputOptions));
return archiveReport.Complete && localReport.Complete && joymeeReport.Complete ? 0 : 1;
