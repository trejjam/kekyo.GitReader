# GitReader

Lightweight Git local repository traversal library.

![GitReader](Images/GitReader.128.png)

# Status

[![Project Status: WIP – Initial development is in progress, but there has not yet been a stable, usable release suitable for the public.](https://www.repostatus.org/badges/latest/wip.svg)](https://www.repostatus.org/#wip)

[![NuGet GitReader](https://img.shields.io/nuget/v/RelaxVersioner.svg?style=flat)](https://www.nuget.org/packages/RelaxVersioner) | [![MyGet RelaxVersioner (devel)](https://img.shields.io/myget/kekyo/v/RelaxVersioner.svg?style=flat&label=myget)](https://www.myget.org/feed/kekyo/package/nuget/RelaxVersioner)

## What is this?

GitReader is a fully-managed Git local repository traversal library for a wide range of .NET environments.
It is lightweight, has a concise, easy-to-use interface, does not depend any other libraries, and does not contain native libraries,
making it suitable for any environment.

It has the following features:

* It provides information on Git branches, tags, and commits.
* Branch tree traversal.
* Read only interface makes immutability.
* Primitive and high-level interfaces ready.
* Only contains 100% managed code. Independent of any external libraries other than the BCL and its compliant libraries.
* Reliable zlib decompression using the .NET standard deflate implementation.

This library was designed from the ground up to replace `libgit2sharp`, on which [RelaxVersioner](https://github.com/kekyo/CenterCLR.RelaxVersioner) depended.
It primarily fits the purpose of easily extracting commit information from a Git repository.

Target .NET platforms are (Almost all!):

* .NET 7.0 to 5.0
* .NET Core 3.1 to 2.0
* .NET Standard 2.1 to 1.6
* .NET Framework 4.8 to 3.5

----

## How to use

Install [GitReader]() from NuGet.

GitReader has high-level interfaces and primitive interfaces.

* The high-level interface is an interface that abstracts the Git repository,
  making it easy to explore branches, tags and commits.
* The primitive interface is an interface that exposes the internal structure of the Git repository as it is,
  It is easy to handle and will be make high-performance with asynchronous operations
  if you know the structure knowledge.

## Samples (High-level interfaces)

TODO:


## Samples (Primitive interfaces)

### Read current head commit

```csharp
using GitReader;
using GitReader.Primitive;

using Repository repository = await Repository.Factory.OpenAsync(
    "/home/kekyo/Projects/YourOwnLocalGitRepo");

Reference head = await repository.GetCurrentHeadReferenceAsync();
Commit commit = await repository.GetCommitAsync(head);

Console.WriteLine($"Hash: {commit.Hash}");
Console.WriteLine($"Author: {commit.Author}");
Console.WriteLine($"Committer: {commit.Committer}");
Console.WriteLine($"Message: {commit.Message}");
```

### Read a branch head commit

```csharp
Reference head = await repository.GetBranchHeadReferenceAsync("develop");
Commit commit = await repository.GetCommitAsync(head);

Console.WriteLine($"Hash: {commit.Hash}");
Console.WriteLine($"Author: {commit.Author}");
Console.WriteLine($"Committer: {commit.Committer}");
Console.WriteLine($"Message: {commit.Message}");
```

### Enumerate branches

```csharp
Reference[] branches = await repository.GetBranchHeadReferencesAsync();

foreach (Reference branch in branches)
{
    Console.WriteLine($"Name: {branch.Name}");
    Console.WriteLine($"Commit: {branch.Commit}");
}
```

### Enumerate tags

```csharp
Reference[] tagReferences = await repository.GetTagReferencesAsync();

foreach (Reference tagReference in tagReferences)
{
    Tag tag = await repository.GetTagAsync(tagReference);

    Console.WriteLine($"Hash: {tag.Hash}");
    Console.WriteLine($"Type: {tag.Type}");
    Console.WriteLine($"Name: {tag.Name}");
    Console.WriteLine($"Tagger: {tag.Tagger}");
    Console.WriteLine($"Message: {tag.Message}");
}
```

### Traverse a branch through primary commits

```csharp
Reference branch = await repository.GetBranchHeadReferenceAsync("develop");
Commit commit = await repository.GetCommitAsync(branch);

while (true)
{
    Console.WriteLine($"Hash: {commit.Hash}");
    Console.WriteLine($"Author: {commit.Author}");
    Console.WriteLine($"Committer: {commit.Committer}");
    Console.WriteLine($"Message: {commit.Message}");

    // Primary parent.
    if (commit.Parents.Length == 0)
    {
        // Bottom of branch.
        break;
    }
    Hash primary = commit.Parents[0];
    commit = await repository.GetCommitAsync(primary);
}
```

----

## License

Apache-v2

