// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Diagnostics;
using Bicep.Core.Modules;
using Bicep.Core.Registry;
using Bicep.Core.Syntax;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.Workspaces;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Bicep.Core.UnitTests.Registry
{
    [TestClass]
    public class ModuleRegistryDispatcherTests
    {
        private static readonly MockRepository Repository = new MockRepository(MockBehavior.Strict);

        [TestMethod]
        public void NoRegistries_AvailableSchemes_ShouldReturnEmpty()
        {
            var dispatcher = CreateDispatcher();
            dispatcher.AvailableSchemes.Should().BeEmpty();
        }

        [TestMethod]
        public void NoRegistries_ValidateModuleReference_ShouldReturnError()
        {
            var module = CreateModule("fakeScheme:fakeModule");
            var dispatcher = CreateDispatcher();
            dispatcher.ValidateModuleReference(module, out var failureBuilder).Should().BeFalse();
            failureBuilder!.Should().NotBeNull();

            using (new AssertionScope())
            {
                failureBuilder!.Should().HaveCodeAndSeverity("BCP189", DiagnosticLevel.Error);
                failureBuilder!.Should().HaveMessage("Module references are not supported in this context.");
            }

            var localModule = CreateModule("test.bicep");
            dispatcher.ValidateModuleReference(localModule, out var localModuleFailureBuilder).Should().BeFalse();
            using (new AssertionScope())
            {
                localModuleFailureBuilder!.Should().HaveCodeAndSeverity("BCP189", DiagnosticLevel.Error);
                localModuleFailureBuilder!.Should().HaveMessage("Module references are not supported in this context.");
            }
        }

        [TestMethod]
        public void NoRegistries_NonValidateMethods_ShouldThrow()
        {
            var module = CreateModule("fakeScheme:fakeModule");
            var dispatcher = CreateDispatcher();

            static void ExpectFailure(Action fail) => fail.Should().Throw<InvalidOperationException>().WithMessage($"The specified module is not valid. Call {nameof(IModuleRegistryDispatcher.ValidateModuleReference)}() first.");

            ExpectFailure(() => dispatcher.IsModuleAvailable(module, out _));
            ExpectFailure(() => dispatcher.TryGetLocalModuleEntryPointUri(new Uri("untitled://two"), module, out _));
            ExpectFailure(() => dispatcher.RestoreModules(new[] { module }));
        }

        [TestMethod]
        public void MockRegistries_AvailableSchemes_ShouldReturnedConfiguredSchemes()
        {
            var first = Repository.Create<IModuleRegistry>();
            first.Setup(m => m.Scheme).Returns("first");

            var second = Repository.Create<IModuleRegistry>();
            second.Setup(m => m.Scheme).Returns("second");

            var dispatcher = CreateDispatcher(first.Object, second.Object);
            dispatcher.AvailableSchemes.Should().BeEquivalentTo("first", "second");
        }

        [TestMethod]
        public void MockRegistries_ModuleLifecycle()
        {
            var fail = Repository.Create<IModuleRegistry>();
            fail.Setup(m => m.Scheme).Returns("fail");

            var mock = Repository.Create<IModuleRegistry>();
            mock.Setup(m => m.Scheme).Returns("mock");

            DiagnosticBuilder.ErrorBuilderDelegate? @null = null;
            var validRef = new MockModuleReference("validRef");
            mock.Setup(m => m.TryParseModuleReference("validRef", out @null))
                .Returns(validRef);

            var validRef2 = new MockModuleReference("validRef2");
            mock.Setup(m => m.TryParseModuleReference("validRef2", out @null))
                .Returns(validRef2);

            var validRef3 = new MockModuleReference("validRef3");
            mock.Setup(m => m.TryParseModuleReference("validRef3", out @null))
                .Returns(validRef3);

            DiagnosticBuilder.ErrorBuilderDelegate? badRefError = x => new ErrorDiagnostic(x.TextSpan, "BCPMock", "Bad ref error");
            mock.Setup(m => m.TryParseModuleReference("badRef", out badRefError))
                .Returns((ModuleReference?)null);

            mock.Setup(m => m.IsModuleRestoreRequired(validRef)).Returns(true);
            mock.Setup(m => m.IsModuleRestoreRequired(validRef2)).Returns(false);
            mock.Setup(m => m.IsModuleRestoreRequired(validRef3)).Returns(true);

            mock.Setup(m => m.TryGetLocalModuleEntryPointPath(It.IsAny<Uri>(), validRef, out @null))
                .Returns(new Uri("untitled://validRef"));
            mock.Setup(m => m.TryGetLocalModuleEntryPointPath(It.IsAny<Uri>(), validRef3, out @null))
                .Returns(new Uri("untitled://validRef3"));

            mock.Setup(m => m.RestoreModules(It.IsAny<IEnumerable<ModuleReference>>()))
                .Returns(new Dictionary<ModuleReference, DiagnosticBuilder.ErrorBuilderDelegate>
                {
                    [validRef3] = x => new ErrorDiagnostic(x.TextSpan, "RegFail", "Failed to restore module")
                });

            var dispatcher = CreateDispatcher(fail.Object, mock.Object);

            var goodModule = CreateModule("mock:validRef");
            var goodModule2 = CreateModule("mock:validRef2");
            var goodModule3 = CreateModule("mock:validRef3");
            var badModule = CreateModule("mock:badRef");

            dispatcher.ValidateModuleReference(goodModule, out var goodValidationBuilder).Should().BeTrue();
            goodValidationBuilder!.Should().BeNull();
            
            dispatcher.ValidateModuleReference(badModule, out var badValidationBuilder).Should().BeFalse();
            badValidationBuilder!.Should().NotBeNull();
            badValidationBuilder!.Should().HaveCodeAndSeverity("BCPMock", DiagnosticLevel.Error);
            badValidationBuilder!.Should().HaveMessage("Bad ref error");

            dispatcher.IsModuleAvailable(goodModule, out var goodAvailabilityBuilder).Should().BeFalse();
            goodAvailabilityBuilder!.Should().HaveCodeAndSeverity("BCP190", DiagnosticLevel.Error);
            goodAvailabilityBuilder!.Should().HaveMessage("The module with reference \"mock:validRef\" has not been restored.");

            dispatcher.IsModuleAvailable(goodModule2, out var goodAvailabilityBuilder2).Should().BeTrue();
            goodAvailabilityBuilder2!.Should().BeNull();

            dispatcher.IsModuleAvailable(goodModule3, out var goodAvailabilityBuilder3).Should().BeFalse();
            goodAvailabilityBuilder3!.Should().HaveCodeAndSeverity("BCP190", DiagnosticLevel.Error);
            goodAvailabilityBuilder3!.Should().HaveMessage("The module with reference \"mock:validRef3\" has not been restored.");

            dispatcher.TryGetLocalModuleEntryPointUri(new Uri("mock://mock"), goodModule, out var entryPointBuilder).Should().Be(new Uri("untitled://validRef"));
            entryPointBuilder!.Should().BeNull();

            dispatcher.TryGetLocalModuleEntryPointUri(new Uri("mock://mock"), goodModule3, out var entryPointBuilder3).Should().Be(new Uri("untitled://validRef3"));
            entryPointBuilder3!.Should().BeNull();

            dispatcher.RestoreModules(new[] { goodModule, goodModule3 }).Should().BeTrue();

            dispatcher.IsModuleAvailable(goodModule3, out var goodAvailabilityBuilder3AfterRestore).Should().BeFalse();
            goodAvailabilityBuilder3AfterRestore!.Should().HaveCodeAndSeverity("RegFail", DiagnosticLevel.Error);
            goodAvailabilityBuilder3AfterRestore!.Should().HaveMessage("Failed to restore module");
        }

        private static IModuleRegistryDispatcher CreateDispatcher(params IModuleRegistry[] registries)
        {
            var provider = Repository.Create<IModuleRegistryProvider>();
            provider.Setup(m => m.Registries).Returns(registries.ToImmutableArray());

            return new ModuleRegistryDispatcher(provider.Object);
        }

        private static ModuleDeclarationSyntax CreateModule(string reference)
        {
            var file = SourceFileFactory.CreateBicepFile(new System.Uri("untitled://hello"), $"module foo '{reference}' = {{}}");
            return file.ProgramSyntax.Declarations.OfType<ModuleDeclarationSyntax>().Single();
        }

        private class MockModuleReference : ModuleReference
        {
            public MockModuleReference(string reference)
                : base("mock")
            {
                this.Reference = reference;
            }

            public string Reference { get; }

            public override string UnqualifiedReference => this.Reference;
        }
    }
}
