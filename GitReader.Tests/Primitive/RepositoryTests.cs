﻿////////////////////////////////////////////////////////////////////////////
//
// GitReader - Lightweight Git local repository traversal library.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
////////////////////////////////////////////////////////////////////////////

using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VerifyNUnit;

namespace GitReader.Primitive;

public sealed class RepositoryTests
{
    [Test]
    public async Task GetCommitDirectly()
    {
        using var repository = await Repository.Factory.OpenAsync(
            RepositoryTestsSetUp.BasePath);

        var commit = await repository.GetCommitAsync(
            Hash.Parse("1205dc34ce48bda28fc543daaf9525a9bb6e6d10"));

        await Verifier.Verify(commit);
    }

    [Test]
    public async Task GetCurrentHead()
    {
        using var repository = await Repository.Factory.OpenAsync(
            RepositoryTestsSetUp.BasePath);

        var head = await repository.GetCurrentHeadReferenceAsync();
        var commit = await repository.GetCommitAsync(head);

        await Verifier.Verify(commit);
    }

    [Test]
    public async Task GetBranchHead()
    {
        using var repository = await Repository.Factory.OpenAsync(
            RepositoryTestsSetUp.BasePath);

        var head = await repository.GetBranchHeadReferenceAsync("master");
        var commit = await repository.GetCommitAsync(head);

        await Verifier.Verify(commit);
    }

    [Test]
    public async Task GetBranchHeads()
    {
        using var repository = await Repository.Factory.OpenAsync(
            RepositoryTestsSetUp.BasePath);

        var branches = await repository.GetBranchHeadReferencesAsync();

        await Verifier.Verify(branches);
    }

    [Test]
    public async Task GetRemoteBranchHeads()
    {
        using var repository = await Repository.Factory.OpenAsync(
            RepositoryTestsSetUp.BasePath);

        var branches = await repository.GetRemoteBranchHeadReferencesAsync();

        await Verifier.Verify(branches);
    }

    [Test]
    public async Task GetTags()
    {
        using var repository = await Repository.Factory.OpenAsync(
            RepositoryTestsSetUp.BasePath);

        var tagReferences = await repository.GetTagReferencesAsync();
        var tags = await Task.WhenAll(
            tagReferences.Select(tagReference => repository.GetTagAsync(tagReference)));

        await Verifier.Verify(tags);
    }

    [Test]
    public async Task TraverseBranchCommits()
    {
        using var repository = await Repository.Factory.OpenAsync(
            RepositoryTestsSetUp.BasePath);

        var branch = await repository.GetBranchHeadReferenceAsync("master");
        var commit = await repository.GetCommitAsync(branch);

        var commits = new List<Commit>();
        while (true)
        {
            commits.Add(commit);

            // Primary parent.
            if (commit.Parents.Length == 0)
            {
                // Bottom of branch.
                break;
            }

            var primary = commit.Parents[0];
            commit = await repository.GetCommitAsync(primary);
        }

        await Verifier.Verify(commits.ToArray());
    }
}
