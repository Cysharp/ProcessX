


    // ProcessX and C# 9.0 Top level statement; like google/zx.

    using Zx;
    using static Zx.Env;

    // `await string` execute process like shell
    await "cat package.json | grep name";

    // receive result msg of stdout
    var branch = await "git branch --show-current";
    await $"dep deploy --branch={branch}";

    // parallel request (similar as Task.WhenAll)
    await new[]
    {
        "echo 1",
        "echo 2",
        "echo 3",
    };

    // you can also use cd(chdir)
    await "cd ../../";

    // run with $"" automatically escaped and quoted
    var dir = "foo/foo bar";
    await run($"mkdir {dir}"); // mkdir "/foo/foo bar"

    // helper for Console.WriteLine and colorize
    log("red log.", ConsoleColor.Red);
    using (color(ConsoleColor.Blue))
    {
        log("blue log");
        Console.WriteLine("also blue");
        await run($"echo {"blue blue blue"}");
    }

    // helper for web request
    var text = await fetchText("http://wttr.in");
    log(text);

    // helper for ReadLine(stdin)
    var bear = await question("What kind of bear is best?");
    log($"You answered: {bear}");









