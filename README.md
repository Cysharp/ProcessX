ProcessX
===
[![CircleCI](https://circleci.com/gh/Cysharp/ProcessX.svg?style=svg)](https://circleci.com/gh/Cysharp/ProcessX)

```csharp
using Cysharp.Diagnostics; // using namespace

await foreach (var item in ProcessX.StartAsync("dotnet build --help"))
{
    Console.WriteLine(item);
}
```