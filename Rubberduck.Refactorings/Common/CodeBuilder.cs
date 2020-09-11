﻿using Rubberduck.Common;
using Rubberduck.Parsing;
using Rubberduck.Parsing.Grammar;
using Rubberduck.Parsing.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Rubberduck.Refactorings
{
    public interface ICodeBuilder
    {
        /// <summary>
        /// Returns ModuleBodyElementDeclaration signature with an ImprovedArgument list
        /// </summary>
        string ImprovedFullMemberSignature(ModuleBodyElementDeclaration declaration);

        /// <summary>
        /// Returns a ModuleBodyElementDeclaration block
        /// with an ImprovedArgument List
        /// </summary>
        /// <param name="content">Main body content/logic of the member</param>
        string BuildMemberBlockFromPrototype(ModuleBodyElementDeclaration declaration,
            string content = null,
            string accessibility = null,
            string newIdentifier = null);

        /// <summary>
        /// Returns the argument list for the input ModuleBodyElementDeclaration with the following improvements:
        /// 1. Explicitly declares Property Let\Set value parameter as ByVal
        /// 2. Ensures UserDefined Type parameters are declared either explicitly or implicitly as ByRef
        /// </summary>
        string ImprovedArgumentList(ModuleBodyElementDeclaration declaration);

        /// <summary>
        /// Generates a Property Get codeblock based on the prototype declaration 
        /// </summary>
        /// <param name="prototype">VariableDeclaration or UserDefinedTypeMember</param>
        /// <param name="content">Member body content.  Formatting is the responsibility of the caller</param>
        /// <param name="parameterIdentifier">Defaults to '<paramref name="propertyIdentifier"/>Value' unless otherwise specified</param>
        bool TryBuildPropertyGetCodeBlock(Declaration prototype,
            string propertyIdentifier,
            out string codeBlock,
            string accessibility = null,
            string content = null);

        /// <summary>
        /// Generates a Property Let codeblock based on the prototype declaration 
        /// </summary>
        /// <param name="prototype">VariableDeclaration or UserDefinedTypeMember</param>
        /// <param name="content">Member body content.  Formatting is the responsibility of the caller</param>
        /// <param name="parameterIdentifier">Defaults to '<paramref name="propertyIdentifier"/>Value' unless otherwise specified</param>
        bool TryBuildPropertyLetCodeBlock(Declaration prototype,
            string propertyIdentifier,
            out string codeBlock,
            string accessibility = null,
            string content = null,
            string parameterIdentifier = null);

        /// <summary>
        /// Generates a Property Set codeblock based on the prototype declaration 
        /// </summary>
        /// <param name="prototype">VariableDeclaration or UserDefinedTypeMember</param>
        /// <param name="content">Member body content.  Formatting is the responsibility of the caller</param>
        /// <param name="parameterIdentifier">Defaults to '<paramref name="propertyIdentifier"/>Value' unless otherwise specified</param>
        bool TryBuildPropertySetCodeBlock(Declaration prototype,
            string propertyIdentifier,
            out string codeBlock,
            string accessibility = null,
            string content = null,
            string parameterIdentifier = null);

        /// <summary>
        /// Generates a UserDefinedType (UDT) declaration using a <c>VariableDeclaration</c> as the prototype for
        /// creating the UserDefinedTypeMember.
        /// </summary>
        /// <remarks>  At least one <c>VariableDeclaration</c> must be provided and 
        /// all <c>UDTMemberIdentifiers</c> must be unique
        /// </remarks>
        /// <param name="memberPrototypes">Collection of prototypes and their required identifier.  Must have 1 or more elements</param>
        string BuildUserDefinedTypeDeclaration(string udtIdentifier, IEnumerable<(VariableDeclaration Field, string UDTMemberIdentifier)> memberPrototypes, Accessibility accessibility = Accessibility.Private);

        string UDTMemberDeclaration(string identifier, string typeName, string indention = null);
    }

    public class CodeBuilder : ICodeBuilder
    {
        public string BuildMemberBlockFromPrototype(ModuleBodyElementDeclaration declaration, 
            string content = null, 
            string accessibility = null, 
            string newIdentifier = null)
        {

            var elements = new List<string>()
            {
                ImprovedFullMemberSignatureInternal(declaration, accessibility, newIdentifier),
                Environment.NewLine,
                string.IsNullOrEmpty(content) ? null : $"{content}{Environment.NewLine}",
                EndStatement(declaration.DeclarationType),
                Environment.NewLine,
            };
            return string.Concat(elements);
        }

        public bool TryBuildPropertyGetCodeBlock(Declaration prototype, string propertyIdentifier, out string codeBlock, string accessibility = null, string content = null)
            => TryBuildPropertyBlockFromTarget(prototype, DeclarationType.PropertyGet, propertyIdentifier, out codeBlock, accessibility, content);

        public bool TryBuildPropertyLetCodeBlock(Declaration prototype, string propertyIdentifier, out string codeBlock, string accessibility = null, string content = null, string parameterIdentifier = null)
            => TryBuildPropertyBlockFromTarget(prototype, DeclarationType.PropertyLet, propertyIdentifier, out codeBlock, accessibility, content, parameterIdentifier);

        public bool TryBuildPropertySetCodeBlock(Declaration prototype, string propertyIdentifier, out string codeBlock, string accessibility = null, string content = null, string parameterIdentifier = null)
            => TryBuildPropertyBlockFromTarget(prototype, DeclarationType.PropertySet, propertyIdentifier, out codeBlock, accessibility, content, parameterIdentifier);

        private bool TryBuildPropertyBlockFromTarget<T>(T prototype, DeclarationType letSetGetType, string propertyIdentifier, out string codeBlock, string accessibility = null, string content = null, string parameterIdentifier = null) where T : Declaration
        {
            codeBlock = string.Empty;
            if (!letSetGetType.HasFlag(DeclarationType.Property))
            {
                throw new ArgumentException();
            }

            if (!(prototype is VariableDeclaration || prototype.DeclarationType.HasFlag(DeclarationType.UserDefinedTypeMember)))
            {
                return false;
            }

            var propertyValueParam = parameterIdentifier ?? Resources.Refactorings.Refactorings.CodeBuilder_DefaultPropertyRHSParam;

            var asType = prototype.IsArray
                ? $"{Tokens.Variant}"
                : IsEnumField(prototype) && prototype.AsTypeDeclaration.Accessibility.Equals(Accessibility.Private)
                    ? $"{Tokens.Long}"
                    : $"{prototype.AsTypeName}";

            var asTypeClause = $"{Tokens.As} {asType}";

            var paramMechanism = IsUserDefinedType(prototype) ? Tokens.ByRef : Tokens.ByVal;

            var letSetParamExpression = $"{paramMechanism} {propertyValueParam} {asTypeClause}";

            codeBlock = letSetGetType.HasFlag(DeclarationType.PropertyGet)
                ? string.Join(Environment.NewLine, $"{accessibility ?? Tokens.Public} {TypeToken(letSetGetType)} {propertyIdentifier}() {asTypeClause}", content, EndStatement(letSetGetType))
                : string.Join(Environment.NewLine, $"{accessibility ?? Tokens.Public} {TypeToken(letSetGetType)} {propertyIdentifier}({letSetParamExpression})", content, EndStatement(letSetGetType));
            return true;
        }

        public string ImprovedFullMemberSignature(ModuleBodyElementDeclaration declaration)
            => ImprovedFullMemberSignatureInternal(declaration);

        private string ImprovedFullMemberSignatureInternal(ModuleBodyElementDeclaration declaration, string accessibility = null, string newIdentifier = null)
        {
            var accessibilityToken = declaration.Accessibility.Equals(Accessibility.Implicit)
                ? Tokens.Public
                : $"{declaration.Accessibility.ToString()}";

            var asTypeName = string.IsNullOrEmpty(declaration.AsTypeName)
                ? string.Empty
                : $" {Tokens.As} {declaration.AsTypeName}";
            
            var elements = new List<string>()
            {
                accessibility ?? accessibilityToken,
                $" {TypeToken(declaration.DeclarationType)} ",
                newIdentifier ?? declaration.IdentifierName,
                $"({ImprovedArgumentList(declaration)})",
                asTypeName
            };
            return string.Concat(elements).Trim();

        }

        public string ImprovedArgumentList(ModuleBodyElementDeclaration declaration)
        {
            var arguments = Enumerable.Empty<string>();
            if (declaration is IParameterizedDeclaration parameterizedDeclaration)
            {
                arguments = parameterizedDeclaration.Parameters
                    .OrderBy(parameter => parameter.Selection)
                    .Select(parameter => BuildParameterDeclaration(
                        parameter,
                        parameter.Equals(parameterizedDeclaration.Parameters.LastOrDefault())
                            && declaration.DeclarationType.HasFlag(DeclarationType.Property)
                            && !declaration.DeclarationType.Equals(DeclarationType.PropertyGet)));
            }
            return $"{string.Join(", ", arguments)}";
        }

        private static string BuildParameterDeclaration(ParameterDeclaration parameter, bool forceExplicitByValAccess)
        {
            var optionalParamType = parameter.IsParamArray
               ? Tokens.ParamArray
               : parameter.IsOptional ? Tokens.Optional : string.Empty;

            var paramMechanism = parameter.IsImplicitByRef
                ? string.Empty
                : parameter.IsByRef ? Tokens.ByRef : Tokens.ByVal;

            if (forceExplicitByValAccess
                && (string.IsNullOrEmpty(paramMechanism) || paramMechanism.Equals(Tokens.ByRef))
                && !IsUserDefinedType(parameter))
            {
                paramMechanism = Tokens.ByVal;
            }

            var name = parameter.IsArray
                ? $"{parameter.IdentifierName}()"
                : parameter.IdentifierName;

            var paramDeclarationElements = new List<string>()
            {
                FormatOptionalElement(optionalParamType),
                FormatOptionalElement(paramMechanism),
                $"{name} ",
                FormatAsTypeName(parameter.AsTypeName),
                FormatDefaultValue(parameter.DefaultValue)
            };

            return string.Concat(paramDeclarationElements).Trim();
        }

        private static string FormatOptionalElement(string element)
            => string.IsNullOrEmpty(element) ? string.Empty : $"{element} ";

        private static string FormatAsTypeName(string AsTypeName) 
            => string.IsNullOrEmpty(AsTypeName) ? string.Empty : $"As {AsTypeName} ";

        private static string FormatDefaultValue(string DefaultValue) 
            => string.IsNullOrEmpty(DefaultValue) ? string.Empty : $"= {DefaultValue}";

        private static Dictionary<DeclarationType, (string TypeToken, string EndStatement)> _declarationTypeTokens
            = new Dictionary<DeclarationType, (string TypeToken, string EndStatement)>()
            {
                [DeclarationType.Function] = (Tokens.Function, $"{Tokens.End} {Tokens.Function}"),
                [DeclarationType.Procedure] = (Tokens.Sub, $"{Tokens.End} {Tokens.Sub}"),
                [DeclarationType.PropertyGet] = ($"{Tokens.Property} {Tokens.Get}", $"{Tokens.End} {Tokens.Property}"),
                [DeclarationType.PropertyLet] = ($"{Tokens.Property} {Tokens.Let}", $"{Tokens.End} {Tokens.Property}"),
                [DeclarationType.PropertySet] = ($"{Tokens.Property} {Tokens.Set}", $"{Tokens.End} {Tokens.Property}"),
            };

        private static string EndStatement(DeclarationType declarationType)
            => _declarationTypeTokens[declarationType].EndStatement;

        private static string TypeToken(DeclarationType declarationType)
            => _declarationTypeTokens[declarationType].TypeToken;

        public string BuildUserDefinedTypeDeclaration(string udtIdentifier, IEnumerable<(VariableDeclaration Field, string UDTMemberIdentifier)> memberPrototypes, Accessibility accessibility = Accessibility.Private)
        {
            if (!memberPrototypes.Any())
            {
                throw new ArgumentOutOfRangeException();
            }

            var hasDuplicateMemberNames = memberPrototypes.Select(pr => pr.UDTMemberIdentifier.ToUpperInvariant())
                .GroupBy(uc => uc).Any(g => g.Count() > 1);
            if (hasDuplicateMemberNames)
            {
                throw new ArgumentException();
            }

            var newMemberTokenPairs = memberPrototypes.Select(m => (GetDeclarationIdentifier(m.Field, m.UDTMemberIdentifier), m.Field.AsTypeName))
                .Cast<(string Identifier, string AsTypeName)>();

            var blockLines = new List<string>();

            blockLines.Add($"{accessibility.TokenString()} {Tokens.Type} {udtIdentifier}");

            blockLines.AddRange(newMemberTokenPairs.Select(m => UDTMemberDeclaration(m.Identifier, m.AsTypeName)));

            blockLines.Add($"{Tokens.End} {Tokens.Type}");

            return string.Join(Environment.NewLine, blockLines);
        }

        private static string GetDeclarationIdentifier(Declaration field, string udtMemberIdentifier)
        {
            if (field.IsArray)
            {
                return field.Context.TryGetChildContext<VBAParser.SubscriptsContext>(out var ctxt)
                    ? $"{udtMemberIdentifier}({ctxt.GetText()})"
                    : $"{udtMemberIdentifier}()";
            }
            return udtMemberIdentifier;
        }

        public string UDTMemberDeclaration(string identifier, string typeName, string indention = null)
            => $"{indention ?? "    "}{identifier} {Tokens.As} {typeName}";

        private static bool IsEnumField(VariableDeclaration declaration)
            => IsMemberVariable(declaration)
                && (declaration.AsTypeDeclaration?.DeclarationType.Equals(DeclarationType.Enumeration) ?? false);

        private static bool IsEnumField(Declaration declaration)
            => IsMemberVariable(declaration)
                && (declaration.AsTypeDeclaration?.DeclarationType.Equals(DeclarationType.Enumeration) ?? false);

        private static bool IsUserDefinedType(Declaration declaration)
            => (declaration.AsTypeDeclaration?.DeclarationType.Equals(DeclarationType.UserDefinedType) ?? false);

        private static bool IsMemberVariable(Declaration declaration)
            => declaration.DeclarationType.HasFlag(DeclarationType.Variable)
                && !declaration.ParentDeclaration.DeclarationType.HasFlag(DeclarationType.Member);
    }
}
