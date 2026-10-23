// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;

namespace Canton.Ledger.Kernel.Validation;

internal static class AddressValidation
{
    internal static ValidationResult? RequireAbsoluteHttpUri(
        string? address, string optionName, string example)
    {
        var isAbsoluteHttpUri = Uri.TryCreate(address, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        return isAbsoluteHttpUri
            ? null
            : new ValidationResult(
                $"{optionName} '{address}' must be an absolute http or https URI, for example '{example}'.",
                [optionName]);
    }
}
