// ProcessX and C# 9.0 Top level statement; like google/zx.
// You can execute script by "dotnet run"

using System;
using Zx;
using static Zx.Env;

log("standard log.");
log("red log.", ConsoleColor.Red);

using (color(ConsoleColor.Blue))
{
    log("blue log");
    Console.WriteLine("Blue Blue");
    await "echo blue blue blue";
}

var bear = await question("What kind of bear is best?");
System.Console.WriteLine($"You answered: {bear}");



await sleep(10);

// await $"cat package.json | grep name";

var branch = await "git branch --show-current";

await $"dep deploy --branch={branch}";



// WhenAll
await new[]
{
    new[]{"echo 1" },
    new[]{"echo 2" },
    new[]{"echo 3" },
};

// WhenAll in sequential command.
await new[]
{
    new[]{"sleep 1", "echo 1" },
    new[]{"sleep 2", "echo 2" },
    new[]{"sleep 3", "echo 3" },
};

var name = "foo bar";
await $"mkdir /foo/{name}";