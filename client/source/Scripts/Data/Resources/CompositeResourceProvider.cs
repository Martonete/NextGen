#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace ArgentumNextgen.Data.Resources;

/// <summary>
/// Chains multiple IResourceProvider instances with priority ordering.
/// First provider that contains an entry wins. Supports tombstone detection
/// to prevent fallthrough to lower-priority providers.
/// </summary>
public class CompositeResourceProvider : IResourceProvider, IDisposable
{
    private readonly List<IResourceProvider> _providers; // ordered by priority (highest first)

    public string BasePath { get; }

    public CompositeResourceProvider(string basePath, List<IResourceProvider> providers)
    {
        BasePath = basePath;
        _providers = providers;
    }

    public bool Exists(string relativePath)
    {
        foreach (var provider in _providers)
        {
            if (provider is AopakResourceProvider aopak && aopak.IsTombstone(relativePath))
                return false; // tombstone stops search — entry was intentionally deleted

            if (provider.Exists(relativePath))
                return true;
        }
        return false;
    }

    public byte[] ReadBytes(string relativePath)
    {
        foreach (var provider in _providers)
        {
            if (provider is AopakResourceProvider aopak && aopak.IsTombstone(relativePath))
                throw new System.IO.FileNotFoundException(
                    $"Entry has been deleted (tombstone): {relativePath}");

            if (provider.Exists(relativePath))
                return provider.ReadBytes(relativePath);
        }
        throw new System.IO.FileNotFoundException($"Entry not found in any provider: {relativePath}");
    }

    public Image? ReadImage(string relativePath)
    {
        foreach (var provider in _providers)
        {
            if (provider is AopakResourceProvider aopak && aopak.IsTombstone(relativePath))
                return null; // tombstone stops search

            if (provider.Exists(relativePath))
                return provider.ReadImage(relativePath);
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            if (provider is IDisposable d) d.Dispose();
        }
        _providers.Clear();
    }
}
