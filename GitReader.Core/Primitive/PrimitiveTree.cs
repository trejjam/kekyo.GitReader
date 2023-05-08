﻿////////////////////////////////////////////////////////////////////////////
//
// GitReader - Lightweight Git local repository traversal library.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
////////////////////////////////////////////////////////////////////////////

using GitReader.Internal;
using System;
using System.Linq;

namespace GitReader.Primitive;

[Flags]
public enum PrimitiveModeFlags
{
    File = 0x8000,
    Directory = 0x4000,
    OwnerRead = 0x0100,
    OwnerWrite = 0x0080,
    OwnerExecute = 0x0040,
    GroupRead = 0x0020,
    GroupWrite = 0x0010,
    GroupExecute = 0x0008,
    OtherRead = 0x0004,
    OtherWrite = 0x0002,
    OtherExecute = 0x0001,
}

public readonly struct PrimitiveTreeEntry : IEquatable<PrimitiveTreeEntry>
{
    public readonly Hash Hash;
    public readonly string Name;
    public readonly PrimitiveModeFlags Modes;

    public PrimitiveTreeEntry(
        Hash hash,
        string name,
        PrimitiveModeFlags modes)
    {
        this.Hash = hash;
        this.Name = name;
        this.Modes = modes;
    }

    public bool Equals(PrimitiveTreeEntry rhs) =>
        this.Hash.Equals(rhs.Hash) &&
        this.Name.Equals(rhs.Name) &&
        this.Modes == rhs.Modes;

    bool IEquatable<PrimitiveTreeEntry>.Equals(PrimitiveTreeEntry rhs) =>
        this.Equals(rhs);

    public override bool Equals(object? obj) =>
        obj is PrimitiveTreeEntry rhs && this.Equals(rhs);

    public override int GetHashCode() =>
        this.Hash.GetHashCode() ^
        this.Name.GetHashCode() ^
        this.Modes.GetHashCode();

    public override string ToString() =>
        $"{this.Modes}: {this.Name}: {this.Hash}";

    public static implicit operator Hash(PrimitiveTreeEntry entry) =>
        entry.Hash;
}

public readonly struct PrimitiveTree : IEquatable<PrimitiveTree>
{
    public readonly Hash Hash;
    public readonly PrimitiveTreeEntry[] Children;

    public PrimitiveTree(
        Hash hash,
        PrimitiveTreeEntry[] children)
    {
        this.Hash = hash;
        this.Children = children;
    }

    public bool Equals(PrimitiveTree rhs) =>
        this.Hash.Equals(rhs.Hash) &&
        (this.Children?.SequenceEqual(rhs.Children ?? Utilities.Empty<PrimitiveTreeEntry>()) ?? false);

    bool IEquatable<PrimitiveTree>.Equals(PrimitiveTree rhs) =>
        this.Equals(rhs);

    public override bool Equals(object? obj) =>
        obj is PrimitiveTree rhs && this.Equals(rhs);

    public override int GetHashCode() =>
        this.Children.Aggregate(
            this.Hash.GetHashCode(),
            (agg, v) => agg ^ v.GetHashCode());

    public override string ToString() =>
        $"{this.Hash}: Children={this.Children.Length}";

    public static implicit operator Hash(PrimitiveTree tree) =>
        tree.Hash;
}
