﻿using System;
using System.Collections.Generic;
using System.Linq;
using ApiCatalog.Markup;

namespace ApiCatalog.CatalogModel;

public readonly struct ApiDeclarationModel : IEquatable<ApiDeclarationModel>
{
    private readonly ApiModel _api;
    private readonly int _offset;

    internal ApiDeclarationModel(ApiModel api, int offset)
    {
        _api = api;
        _offset = offset;
    }

    public ApiCatalogModel Catalog => _api.Catalog;

    public ApiModel Api => _api;

    public AssemblyModel Assembly
    {
        get
        {
            var assemblyOffset = _api.Catalog.GetApiTableInt32(_offset);
            return new AssemblyModel(_api.Catalog, assemblyOffset);
        }
    }

    private Markup.Markup GetMyMarkup()
    {
        var markupOffset = _api.Catalog.GetApiTableInt32(_offset + 4);
        return _api.Catalog.GetMarkup(markupOffset);
    }

    public Markup.Markup GetMarkup()
    {
        var markups = new List<Markup.Markup>();
        var current = Api.Parent;
        var assembly = Assembly;
        while (current != default)
        {
            var declaration = current.Declarations.Single(d => d.Assembly == assembly);
            markups.Add(declaration.GetMyMarkup());
            current = current.Parent;
        }
        markups.Reverse();
        markups.Add(GetMyMarkup());

        var parts = new List<MarkupPart>();

        var indent = 0;

        foreach (var markup in markups)
        {
            if (indent > 0)
            {
                if (indent - 1 > 0)
                    parts.Add(new MarkupPart(MarkupPartKind.Whitespace, new string(' ', 4 * (indent - 1))));
                parts.Add(new MarkupPart(MarkupPartKind.Punctuation, "{"));
                parts.Add(new MarkupPart(MarkupPartKind.Whitespace, "\n"));
            }

            var needsIndent = true;

            foreach (var part in markup.Parts)
            {
                if (needsIndent)
                {
                    // Add indentation
                    parts.Add(new MarkupPart(MarkupPartKind.Whitespace, new string(' ', 4 * indent)));
                    needsIndent = false;
                }

                parts.Add(part);

                if (part.Kind == MarkupPartKind.Whitespace && part.Text == "\n")
                    needsIndent = true;
            }

            parts.Add(new MarkupPart(MarkupPartKind.Whitespace, "\n"));

            indent++;
        }

        for (var i = markups.Count - 1 - 1; i >= 0; i--)
        {
            if (i > 0)
                parts.Add(new MarkupPart(MarkupPartKind.Whitespace, new string(' ', 4 * i)));
            parts.Add(new MarkupPart(MarkupPartKind.Punctuation, "}"));
            parts.Add(new MarkupPart(MarkupPartKind.Whitespace, "\n"));
        }

        return new Markup.Markup(parts);
    }

    public override bool Equals(object obj)
    {
        return obj is ApiDeclarationModel model && Equals(model);
    }

    public bool Equals(ApiDeclarationModel other)
    {
        return ReferenceEquals(_api, other._api) &&
               _offset == other._offset;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_api, _offset);
    }

    public static bool operator ==(ApiDeclarationModel left, ApiDeclarationModel right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ApiDeclarationModel left, ApiDeclarationModel right)
    {
        return !(left == right);
    }
}