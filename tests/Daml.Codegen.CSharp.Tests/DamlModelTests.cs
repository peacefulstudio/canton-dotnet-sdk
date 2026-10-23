// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Tests for the Daml model types used in DAR parsing.
/// </summary>
public class DamlModelTests
{
    #region DamlType Tests

    [Fact]
    public void DamlTypeRef_should_store_package_module_name()
    {
        var typeRef = new DamlTypeRef("pkg-123", "My.Module", "MyType");

        typeRef.PackageId.Should().Be("pkg-123");
        typeRef.Module.Should().Be("My.Module");
        typeRef.Name.Should().Be("MyType");
    }

    [Fact]
    public void DamlTypeApp_should_store_base_and_arguments()
    {
        var baseType = new DamlPrimitiveType(DamlPrimitive.TextMap);
        var argType = new DamlPrimitiveType(DamlPrimitive.Int64);

        var typeApp = new DamlTypeApp(baseType, [argType]);

        typeApp.Base.Should().Be(baseType);
        typeApp.Arguments.Should().HaveCount(1);
        typeApp.Arguments[0].Should().Be(argType);
    }

    #endregion

    #region DamlPrimitive Enum Tests

    [Fact]
    public void DamlPrimitive_should_have_all_types()
    {
        var primitives = new[]
        {
            DamlPrimitive.Unit,
            DamlPrimitive.Bool,
            DamlPrimitive.Int64,
            DamlPrimitive.Numeric,
            DamlPrimitive.Text,
            DamlPrimitive.Date,
            DamlPrimitive.Timestamp,
            DamlPrimitive.Party,
            DamlPrimitive.ContractId,
            DamlPrimitive.List,
            DamlPrimitive.Optional,
            DamlPrimitive.TextMap,
            DamlPrimitive.GenMap
        };

        primitives.Should().HaveCount(13);

        foreach (var primitive in primitives)
        {
            var type = new DamlPrimitiveType(primitive);
            type.Primitive.Should().Be(primitive);
        }
    }

    #endregion

    #region DamlDataType Tests

    [Fact]
    public void DamlRecordDefinition_should_store_fields()
    {
        var fields = new[]
        {
            new DamlFieldDefinition("name", new DamlPrimitiveType(DamlPrimitive.Text)),
            new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Int64))
        };

        var definition = new DamlRecordDefinition(fields);

        definition.Fields.Should().HaveCount(2);
        definition.Fields[0].Name.Should().Be("name");
        definition.Fields[1].Name.Should().Be("value");
    }

    [Fact]
    public void DamlVariantDefinition_should_store_constructors()
    {
        var constructors = new[]
        {
            new DamlVariantConstructor("Left", new DamlPrimitiveType(DamlPrimitive.Text)),
            new DamlVariantConstructor("Right", new DamlPrimitiveType(DamlPrimitive.Int64))
        };

        var definition = new DamlVariantDefinition(constructors);

        definition.Constructors.Should().HaveCount(2);
        definition.Constructors[0].Name.Should().Be("Left");
        definition.Constructors[1].Name.Should().Be("Right");
    }

    [Fact]
    public void DamlVariantConstructor_should_store_name_and_type()
    {
        var constructor = new DamlVariantConstructor("Some", new DamlPrimitiveType(DamlPrimitive.Text));

        constructor.Name.Should().Be("Some");
        constructor.ArgumentType.Should().NotBeNull();
        constructor.ArgumentType.Should().BeOfType<DamlPrimitiveType>();
    }

    [Fact]
    public void DamlVariantConstructor_should_allow_null_argument()
    {
        var constructor = new DamlVariantConstructor("None", null);

        constructor.Name.Should().Be("None");
        constructor.ArgumentType.Should().BeNull();
    }

    [Fact]
    public void DamlEnumDefinition_should_store_constructors()
    {
        var definition = new DamlEnumDefinition(["Red", "Green", "Blue"]);

        definition.Constructors.Should().HaveCount(3);
        definition.Constructors.Should().Contain("Red");
        definition.Constructors.Should().Contain("Green");
        definition.Constructors.Should().Contain("Blue");
    }

    [Fact]
    public void DamlDataType_should_store_name_and_definition()
    {
        var recordDef = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))]);

        var dataType = new DamlDataType
        {
            Name = "MyRecord",
            Definition = recordDef
        };

        dataType.Name.Should().Be("MyRecord");
        dataType.Definition.Should().Be(recordDef);
    }

    #endregion

    #region DamlFieldDefinition Tests

    [Fact]
    public void DamlFieldDefinition_should_store_name_and_type()
    {
        var field = new DamlFieldDefinition("myField", new DamlPrimitiveType(DamlPrimitive.Bool));

        field.Name.Should().Be("myField");
        field.Type.Should().BeOfType<DamlPrimitiveType>();
        ((DamlPrimitiveType)field.Type).Primitive.Should().Be(DamlPrimitive.Bool);
    }

    #endregion

    #region DamlTemplate Tests

    [Fact]
    public void DamlTemplate_should_store_all_properties()
    {
        var choices = new[]
        {
            new DamlChoice
            {
                Name = "Archive",
                Consuming = true,
                ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
            }
        };

        var template = new DamlTemplate
        {
            Name = "MyTemplate",
            Choices = choices,
            Key = new DamlPrimitiveType(DamlPrimitive.Party),
            Implements =
            [
                new DamlTypeRef("pkg1", "Module1", "Interface1"),
                new DamlTypeRef("pkg2", "Module2", "Interface2"),
            ]
        };

        template.Name.Should().Be("MyTemplate");
        template.Choices.Should().HaveCount(1);
        template.Key.Should().NotBeNull();
        template.Implements.Should().HaveCount(2);
    }

    [Fact]
    public void DamlTemplate_Key_should_be_optional()
    {
        var template = new DamlTemplate
        {
            Name = "KeylessTemplate",
            Choices = []
        };

        template.Key.Should().BeNull();
    }

    [Fact]
    public void DamlTemplate_Implements_should_default_to_empty()
    {
        var template = new DamlTemplate
        {
            Name = "SimpleTemplate",
            Choices = []
        };

        template.Implements.Should().BeEmpty();
    }

    #endregion

    #region DamlChoice Tests

    [Fact]
    public void DamlChoice_should_store_all_properties()
    {
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", "Module", "TransferArgs"),
            ReturnType = new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.ContractId),
                [new DamlTypeRef("", "Module", "Asset")])
        };

        choice.Name.Should().Be("Transfer");
        choice.Consuming.Should().BeTrue();
        choice.ArgumentType.Should().BeOfType<DamlTypeRef>();
        choice.ReturnType.Should().BeOfType<DamlTypeApp>();
    }

    [Fact]
    public void DamlChoice_Consuming_should_be_false_for_non_consuming()
    {
        var choice = new DamlChoice
        {
            Name = "View",
            Consuming = false,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Text)
        };

        choice.Consuming.Should().BeFalse();
    }

    #endregion

    #region DamlInterface Tests

    [Fact]
    public void DamlInterface_should_store_name_and_methods()
    {
        var methods = new[]
        {
            new DamlChoice
            {
                Name = "GetBalance",
                Consuming = false,
                ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric)
            }
        };

        var iface = new DamlInterface
        {
            Name = "Holdable",
            Choices = methods,
            ViewType = new DamlTypeRef("", "Module", "HoldableView")
        };

        iface.Name.Should().Be("Holdable");
        iface.Choices.Should().HaveCount(1);
        iface.ViewType.Should().NotBeNull();
    }

    [Fact]
    public void DamlInterface_ViewType_should_be_optional()
    {
        var iface = new DamlInterface
        {
            Name = "Viewless",
            Choices = []
        };

        iface.ViewType.Should().BeNull();
    }

    [Fact]
    public void DamlInterfaceMethod_should_carry_name_and_return_type()
    {
        var method = new DamlInterfaceMethod("holder", new DamlPrimitiveType(DamlPrimitive.Party));
        var equalMethod = new DamlInterfaceMethod("holder", new DamlPrimitiveType(DamlPrimitive.Party));

        method.Name.Should().Be("holder");
        method.ReturnType.Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
        method.Should().Be(equalMethod,
            "DamlInterfaceMethod is a record: two instances with an equal name and an equal "
            + "return type must compare equal structurally");
    }

    [Fact]
    public void DamlInterface_Methods_should_default_to_empty()
    {
        var iface = new DamlInterface
        {
            Name = "Methodless",
            Choices = []
        };

        iface.Methods.Should().NotBeNull(
            "a freshly constructed DamlInterface without Methods set must yield an empty list, "
            + "never null — existing construction sites (the .NET DalfReader producer among them) "
            + "set no Methods and must not be forced to change");
        iface.Methods.Should().BeEmpty();
    }

    #endregion

    #region DamlPackage Tests

    [Fact]
    public void DamlPackage_should_store_all_properties()
    {
        var modules = new[]
        {
            new DamlModule
            {
                Name = "Test.Module",
                Templates = [],
                DataTypes = [],
                Interfaces = []
            }
        };

        var package = new DamlPackage
        {
            PackageId = "abc123",
            Name = "my-package",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

        package.PackageId.Should().Be("abc123");
        package.Name.Should().Be("my-package");
        package.Version.Should().Be(new Version(1, 2, 3));
        package.LfVersion.Should().Be("2.1");
        package.Modules.Should().HaveCount(1);
    }

    #endregion

    #region DamlModule Tests

    [Fact]
    public void DamlModule_should_store_all_collections()
    {
        var templates = new[]
        {
            new DamlTemplate { Name = "Template1", Choices = [] }
        };
        var dataTypes = new[]
        {
            new DamlDataType { Name = "Type1", Definition = new DamlRecordDefinition([]) }
        };
        var interfaces = new[]
        {
            new DamlInterface { Name = "Interface1", Choices = [] }
        };

        var module = new DamlModule
        {
            Name = "Full.Module",
            Templates = templates,
            DataTypes = dataTypes,
            Interfaces = interfaces
        };

        module.Name.Should().Be("Full.Module");
        module.Templates.Should().HaveCount(1);
        module.DataTypes.Should().HaveCount(1);
        module.Interfaces.Should().HaveCount(1);
    }

    #endregion

    #region DarModel Tests

    [Fact]
    public void IDarSource_does_not_force_implementations_to_carry_dependency_reference_resolution()
    {
        typeof(IDarSource).GetMethod("ResolveAllDependencyReferences").Should().BeNull(
            "the member was leftover two-phase-init scaffolding with an empty proto-path implementation; " +
            "custom IDarSource implementations must not be forced to implement it");
    }

    [Fact]
    public void DarModel_AllPackages_should_include_main_and_dependencies()
    {
        var mainPackage = new DamlPackage
        {
            PackageId = "main-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var dep1 = new DamlPackage
        {
            PackageId = "dep1-id",
            Name = "dep1",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var dep2 = new DamlPackage
        {
            PackageId = "dep2-id",
            Name = "dep2",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var dar = new DarModel
        {
            MainPackage = mainPackage,
            Dependencies = [dep1, dep2]
        };

        var allPackages = ((IDarSource)dar).AllPackages.ToList();
        allPackages.Should().HaveCount(3);
        allPackages[0].Should().Be(mainPackage);
        allPackages[1].Should().Be(dep1);
        allPackages[2].Should().Be(dep2);
    }

    [Fact]
    public void DarModel_AllPackages_should_work_with_no_dependencies()
    {
        var mainPackage = new DamlPackage
        {
            PackageId = "main-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var dar = new DarModel
        {
            MainPackage = mainPackage,
            Dependencies = []
        };

        var allPackages = ((IDarSource)dar).AllPackages.ToList();
        allPackages.Should().HaveCount(1);
        allPackages[0].Should().Be(mainPackage);
    }

    #endregion
}
