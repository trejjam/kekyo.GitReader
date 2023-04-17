﻿////////////////////////////////////////////////////////////////////////////
//
// GitReader - Lightweight Git local repository exploration library.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
////////////////////////////////////////////////////////////////////////////

namespace GitReader.Primitive;

public readonly struct Reference
{
    public readonly string Name;
    public readonly Hash Target;

    private Reference(
        string name,
        Hash target)
    {
        Name = name;
        Target = target;
    }

    public override string ToString() =>
        $"{this.Name}: {this.Target}";

    public void Deconstruct(
        out string name, out Hash target)
    {
        name = this.Name;
        target = this.Target;
    }

    public static implicit operator Hash(Reference reference) =>
        reference.Target;

    public static Reference Create(
        string name,
        Hash target) =>
        new(name, target);
}
