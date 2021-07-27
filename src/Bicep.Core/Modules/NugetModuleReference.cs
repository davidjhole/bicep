// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Diagnostics;
using System;
using System.Collections.Generic;

namespace Bicep.Core.Modules
{
    /// <summary>
    /// Represents a reference to a NuGet package.
    /// </summary>
    public class NugetModuleReference : ModuleReference
    {
        // these exist to keep equals and hashcode implementations in sync
        // NuGet package IDs are case-insensitive
        private static readonly IEqualityComparer<string> PackageIdComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly IEqualityComparer<string> VersionComparer = StringComparer.OrdinalIgnoreCase;

        private NugetModuleReference(string packageId, string version)
            : base("nuget")
        {
            this.PackageId = packageId;
            this.Version = version;
        }

        /// <summary>
        /// Gets the package identifier.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Gets the package version.
        /// </summary>
        public string Version { get; }

        public override bool Equals(object obj)
        {
            if(obj is not NugetModuleReference other)
            {
                return false;
            }

            return
                PackageIdComparer.Equals(this.PackageId, other.PackageId) &&
                VersionComparer.Equals(this.Version, other.Version);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(this.PackageId, PackageIdComparer);
            hash.Add(this.Version, VersionComparer);

            return hash.ToHashCode();
        }

        public override string UnqualifiedReference => $"{this.PackageId}@{this.Version}";

        public static NugetModuleReference? TryParse(string rawValue, out DiagnosticBuilder.ErrorBuilderDelegate? failureBuilder)
        {
            var parts = rawValue.Split('@', 2, StringSplitOptions.None);
            switch(parts.Length)
            {
                case 2:
                    // TODO: Add validation
                    failureBuilder = null;
                    return new NugetModuleReference(parts[0], parts[1]);

                default:
                    failureBuilder = x => x.InvalidNuGetPackageReference($"nuget:{rawValue}");
                    return null;
            };
        }
    }
}
