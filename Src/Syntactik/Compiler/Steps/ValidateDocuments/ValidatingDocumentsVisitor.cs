﻿#region license
// Copyright © 2017 Maxim O. Trushin (trushin@gmail.com)
//
// This file is part of Syntactik.
// Syntactik is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// Syntactik is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.

// You should have received a copy of the GNU Lesser General Public License
// along with Syntactik.  If not, see <http://www.gnu.org/licenses/>.
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using Syntactik.Compiler.Generator;
using Syntactik.DOM;
using Syntactik.DOM.Mapped;
using Alias = Syntactik.DOM.Mapped.Alias;
using AliasDefinition = Syntactik.DOM.Mapped.AliasDefinition;
using Argument = Syntactik.DOM.Mapped.Argument;
using Comment = Syntactik.DOM.Comment;
using Document = Syntactik.DOM.Mapped.Document;
using Element = Syntactik.DOM.Mapped.Element;
using Module = Syntactik.DOM.Mapped.Module;
using NamespaceDefinition = Syntactik.DOM.Mapped.NamespaceDefinition;
using Parameter = Syntactik.DOM.Mapped.Parameter;
using Scope = Syntactik.DOM.Scope;
using ValueType = Syntactik.DOM.Mapped.ValueType;


namespace Syntactik.Compiler.Steps
{
    public class ValidatingDocumentsVisitor: AliasResolvingVisitor
    {   
        private bool _blockStateUnknown;
        private Stack<JsonGenerator.BlockState> _blockState;
        private Module _currentModule;
        private readonly Stack<ChoiceInfo> _choiceStack = new Stack<ChoiceInfo>();
        private readonly NamespaceResolver _namespaceResolver;

        public ValidatingDocumentsVisitor(CompilerContext context):base(context)
        {
            _namespaceResolver = (NamespaceResolver)Context.Properties["NamespaceResolver"];
        }

        public override void OnModule(DOM.Module module)
        {
            _currentModule = (Module) module;
            _namespaceResolver.EnterModule(module);
            _blockStateUnknown = true;
            _blockState = new Stack<JsonGenerator.BlockState>();

            Visit(module.NamespaceDefinitions);
            Visit(module.Members.Where(
                    m => (m is DOM.AliasDefinition) ||
                    ((Document)m).Module.ModuleDocument != m ||
                    ((IContainer)m).Entities.Any(e => !(e is DOM.Comment))) //Skipping module documents having only comments in body
                );

        }

        public override void OnDocument(DOM.Document document)
        {
            _currentDocument = (Document) document;
            CheckPairValue(document);
            _blockStateUnknown = true;

            CheckDocumentNameDuplicates(document);
            _choiceStack.Push(((Document)document).ChoiceInfo);
            base.OnDocument(document);
            if (_currentModule.TargetFormat == Module.TargetFormats.Xml)
                CheckDocumentElement(document);
            _choiceStack.Pop();
            if (((Document)document).ChoiceInfo.Errors != null)
                foreach (var error in ((Document)document).ChoiceInfo.Errors)
                    Context.AddError(error);
            _currentDocument = null;
        }

        /// <summary>
        /// Checks if XML document has one root element
        /// </summary>
        /// <param name="document"></param>
        private void CheckDocumentElement(DOM.Document document)
        {
            int rootElementCount = 0;
            foreach (var entity in document.Entities)
            {
                if (entity is DOM.Element) rootElementCount++;

                var scope = entity as Scope;
                if (scope != null) rootElementCount += CalcNumOfRootElements(scope);

                if (entity is DOM.Alias) rootElementCount += CalcNumOfRootElements((Alias)entity);

                if (rootElementCount > 1)break;
            }
            if (rootElementCount == 0)
            {
                Context.AddError(CompilerErrorFactory.DocumentMustHaveOneRootElement((Document)document,
                    document.Module.FileName, " at least"));
            }
            else if (rootElementCount > 1)
            {
                Context.AddError(CompilerErrorFactory.DocumentMustHaveOneRootElement((Document)document,
                    document.Module.FileName, " only"));
            }
        }

        private int CalcNumOfRootElements(Scope scope)
        {
            var rootElementCount = 0;
            foreach (var entity in scope.Entities)
            {
                if (entity is DOM.Element) rootElementCount++;

                var scopeEntity = entity as Scope;
                if (scopeEntity != null) rootElementCount += CalcNumOfRootElements(scopeEntity);

                if (entity is DOM.Alias) rootElementCount += CalcNumOfRootElements((Alias)entity);

                if (rootElementCount > 1) break;
            }
            return rootElementCount;
        }

        private int CalcNumOfRootElements(Alias alias)
        {
            int result = 0;
            var aliasDef = alias.AliasDefinition;
            if (aliasDef == null) return 0;
            if (aliasDef.HasCircularReference) return 0;
            PairCollection<Entity> entities = null;
            if (aliasDef.Delimiter == DelimiterEnum.CC)
            {
                var choiceInfo = JsonGenerator.FindChoiceInfo(_choiceStack.Peek(), alias);
                if (choiceInfo?.Children != null) entities = ((Element) choiceInfo.Children[0].ChoiceNode).Entities;
            }
            else
            {
                entities = aliasDef.Entities;
            }

            if (entities == null) return result;
            foreach (var entity in entities)
            {
                if (entity is DOM.Element)
                {
                    result++;
                    continue;
                }

                var scopeEntity = entity as Scope;
                if (scopeEntity != null)
                {
                    result += CalcNumOfRootElements(scopeEntity);
                    continue;
                }

                var aliasEntity = entity as DOM.Alias;
                if (aliasEntity != null)
                {
                    result += CalcNumOfRootElements((Alias)aliasEntity);
                    continue;
                }
            }
            return result;
        }

        /// <summary>
        /// Checking if the document's name is unique in project.
        /// </summary>
        /// <param name="document"></param>
        private void CheckDocumentNameDuplicates(DOM.Document document)
        {
            var sameNameDocuments =
                _namespaceResolver.ModuleMembersNsInfo.FindAll(
                    n => (n.ModuleMember is DOM.Document && ((DOM.Document) n.ModuleMember).Name == document.Name)
                         &&
                         ((Module) document.Module).TargetFormat ==
                         ((Module) ((DOM.Document) n.ModuleMember).Module).TargetFormat
                         && ((DOM.Document)n.ModuleMember).Entities.Any(e => !(e is DOM.Comment))
                );
            if (sameNameDocuments.Count <= 1) return;
            if (((Document) document).Module.ModuleDocument != document) //don't report error for module document
                Context.AddError(CompilerErrorFactory.DuplicateDocumentName((Document) document, _currentModule.FileName));
        }

        /// <summary>
        /// Checking if the namespace definition name is unique per module/ module member.
        /// </summary>
        /// <param name="namespaceDefinition"></param>
        private void CheckNsDefDuplicates(NamespaceDefinition namespaceDefinition)
        {
            var parentModule = namespaceDefinition.Parent as Module;
            if (parentModule != null)
            {
                var sameNsDefs = parentModule.NamespaceDefinitions.Where(n => n.Name == namespaceDefinition.Name);
                if (sameNsDefs.Count() > 1)
                {
                    Context.AddError(CompilerErrorFactory.DuplicateNsDefName(namespaceDefinition,
                        _currentModule.FileName));
                }
                return;
            }

            var parentModuleMember = namespaceDefinition.Parent as ModuleMember;
            if (parentModuleMember != null)
            {
                var sameNsDefs = parentModuleMember.NamespaceDefinitions.Where(n => n.Name == namespaceDefinition.Name);
                if (sameNsDefs.Count() > 1)
                {
                    Context.AddError(CompilerErrorFactory.DuplicateNsDefName(namespaceDefinition,
                        _currentModule.FileName));
                }
                return;
            }
            //var sameNsDefs = 
            //    _namespaceResolver.ModuleMembersNsInfo.FindAll(
            //        n => (n.ModuleMember is DOM.Document && ((DOM.Document)n.ModuleMember).Name == document.Name)
            //             &&
            //             ((Module)document.Module).TargetFormat ==
            //             ((Module)((DOM.Document)n.ModuleMember).Module).TargetFormat
            //    );
            //if (sameNameDocuments.Count <= 1) return;
            //if (((Document)document).Module.ModuleDocument != document) //don't report error for module document
            //    Context.AddError(CompilerErrorFactory.DuplicateDocumentName((Document)document, _currentModule.FileName));
        }


        public override void OnAlias(DOM.Alias alias)
        {
            CheckBlockIntegrityForValueAlias(alias);
            CheckPairValue(alias);
            CheckAliasIsDefined(alias);
            var aliasDef = ((Alias)alias).AliasDefinition;
            //Do not resolve alias without AliasDef or having circular reference
            if (aliasDef == null || aliasDef.HasCircularReference) return;

            AliasContext.Push(new AliasContext{ AliasDefinition = aliasDef, Alias = (Alias)alias, AliasNsInfo = GetContextNsInfo() });
            CheckForUnexpectedArguments((Alias)alias, aliasDef, _currentModule.FileName);
            CheckInterpolation((Alias)alias);
            if (CheckStartOfChoiceContainer((Alias) alias, aliasDef.Entities, aliasDef))
            {

                Visit(aliasDef.Entities.Where(e => !(e is DOM.Attribute)));
                EndChoiceContainer();
            }
            else
            {
                Visit(aliasDef.Entities.Where(e => !(e is DOM.Attribute)));
            }
                
            AliasContext.Pop();
            _namespaceResolver.ProcessAlias((Alias) alias);
        }


        private void CheckAliasIsDefined(DOM.Alias alias)
        {
            if (((Alias) alias).AliasDefinition == null)
            {
                Context.AddError(CompilerErrorFactory.AliasIsNotDefined((Alias) alias, _currentModule.FileName));
            }
        }

        private void ReportErrorInsideChoice(Func<CompilerError> error)
        {
            if (_choiceStack.Count == 0)
                //Report Error if alias is not defined
                Context.AddError(error());
            else
            {
                var choiceInfo = _choiceStack.Peek();
                choiceInfo.AddError(error());
            }
        }

        private void CheckBlockIntegrityForValueAlias(DOM.Alias alias)
        {
            if (((Alias) alias).AliasDefinition == null) return;
            if (!((Alias)alias).AliasDefinition.IsValueNode) return;
            if (alias?.Parent.Delimiter == DelimiterEnum.CE) return;
            if (_currentModule.TargetFormat == Module.TargetFormats.Xml && alias.Parent is Document)
                Context.AddError(CompilerErrorFactory.InvalidUsageOfValueAlias((Alias) alias, _currentModule.FileName));

            if (_currentModule.TargetFormat == Module.TargetFormats.Xml) return;

            if (!_blockStateUnknown)
            {
                var blockState = _blockState.Peek();
                if (blockState != JsonGenerator.BlockState.Object) return;

                ReportErrorForEachNodeInAliasContext(
                    n => CompilerErrorFactory.PropertyIsExpected((IMappedPair)n, _currentModule.FileName));
                Context.AddError(CompilerErrorFactory.PropertyIsExpected((IMappedPair)alias, _currentModule.FileName));
                return;
            }

            //This element is the first element of the block. It decides if the block is array or object
            _blockState.Push(JsonGenerator.BlockState.Array);
            _blockStateUnknown = false;
        }

        public override void OnAliasDefinition(DOM.AliasDefinition aliasDefinition)
        {
            CheckPairValue(aliasDefinition);

            _blockStateUnknown = true;
            CheckAliasDefNameUniqueness(aliasDefinition);
            var choiceInfo = new ChoiceInfo(null, null);
            _choiceStack.Push(choiceInfo);
            Visit(aliasDefinition.NamespaceDefinitions);
            Visit(aliasDefinition.Entities);
            _choiceStack.Pop();
            if (choiceInfo.Children == null && choiceInfo.Errors != null)
                foreach (var error in choiceInfo.Errors)
                    Context.AddError(error);

        }

        private void CheckAliasDefNameUniqueness(DOM.AliasDefinition aliasDefinition)
        {
            var sameNameAliasDef =
                _namespaceResolver.ModuleMembersNsInfo.FindAll(
                    n =>
                        (n.ModuleMember is DOM.AliasDefinition &&
                         ((DOM.AliasDefinition) n.ModuleMember).Name == aliasDefinition.Name));
            if (sameNameAliasDef.Count > 1)
            {
                if (sameNameAliasDef.Count == 2)
                {
                    //Reporting error for 2 documents (existing and new)
                    var prevAliasDef = (DOM.AliasDefinition) sameNameAliasDef[0].ModuleMember;
                    Context.AddError(CompilerErrorFactory.DuplicateAliasDefName((AliasDefinition) prevAliasDef,
                        prevAliasDef.Module.FileName));
                }
                Context.AddError(CompilerErrorFactory.DuplicateAliasDefName((AliasDefinition) aliasDefinition,
                    _currentModule.FileName));
            }
        }

        public override void OnElement(DOM.Element element)
        {
            if (!((Element) element).IsChoice)
            {
                CheckBlockIntegrity(element);
                CheckPairValue(element);
                CheckArrayItem((Element) element);
                CheckInterpolation((Element)element);
            }
            _blockStateUnknown = true;
            var prevBlockStateCount = _blockState.Count;
            EnterChoiceNode(element);
            if (((Element) element).IsChoice)
            {
                CheckInterpolation((Element)element);
            }
            if (CheckStartOfChoiceContainer(element, element.Entities))
            {
                base.OnElement(element);
                EndChoiceContainer();
            }
            else
            {
                CheckArrayDelimiter(element);
                base.OnElement(element);
            }
            ExitChoiceNode(element);
            _blockStateUnknown = false;

            if (_blockState.Count > prevBlockStateCount)
                _blockState.Pop();
        }

        private void CheckArrayDelimiter(Pair element)
        {
            if (element.Delimiter == DelimiterEnum.CCC)
            {
                _blockState.Push(JsonGenerator.BlockState.Array);
                _blockStateUnknown = false;
            }
        }

        private void EnterChoiceNode(DOM.Element element)
        {
            if (!((IChoiceNode)element).IsChoice) return;
            ChoiceInfo parent = _choiceStack.Count == 0 ? null : _choiceStack.Peek();
            var choiceInfo = new ChoiceInfo(parent, element);
            _choiceStack.Push(choiceInfo);

        }

        /// <summary>
        /// If choice fails then it adds all error to the parent container,
        /// otherwise the choice adds itself to the parent child list.
        /// </summary>
        /// <param name="element"></param>
        private void ExitChoiceNode(DOM.Element element)
        {
            if (((IChoiceNode) element).IsChoice)
            {
                var choice = _choiceStack.Pop();
                if (choice.Errors != null)
                {
                    foreach (var error in choice.Errors)
                    {
                        choice.Parent.AddError(error);
                    }
                }
                else if (choice.Parent.Children == null)
                    choice.Parent.AddChild(choice);
            }
        }

        /// <summary>
        /// Checks if Choice Container should be created and creates it.
        /// </summary>
        /// <param name="pair">Current pair from visitor.</param>
        /// <param name="entities">Pairs enitities. Empty pair is ignored because there are no choices.</param>
        /// <param name="implementationPair">Actual implementation of pair, (AliasDef is implementation of Alias for example )</param>
        /// <returns>True if Choice Conteiner is found.</returns>
        private bool CheckStartOfChoiceContainer(Pair pair, PairCollection<Entity> entities, Pair implementationPair = null)
        {
            if (implementationPair == null) implementationPair = pair;
            if (implementationPair.Delimiter != DelimiterEnum.CC && implementationPair.Delimiter != DelimiterEnum.ECC || entities == null || entities.Count == 0) return false;
            ChoiceInfo parent = _choiceStack.Count == 0 ? null : _choiceStack.Peek();
            var choiceInfo = new ChoiceInfo(parent, pair);
            _choiceStack.Push(choiceInfo);
            return true;
        }

        private void EndChoiceContainer()
        {
            var choiceContainer = _choiceStack.Pop();
            if (choiceContainer.Children == null && choiceContainer.Errors != null)
            {
                if (_choiceStack.Count == 0)
                {
                    foreach (var error in choiceContainer.Errors)
                    {
                        Context.AddError(error);
                    }
                }
                else
                {
                    foreach (var error in choiceContainer.Errors)
                    {
                        choiceContainer.Parent.AddError(error);
                    }
                }
            }
            else
            {
                if (_choiceStack.Count > 0) _choiceStack.Peek().AddChild(choiceContainer);
            }
        }

        private void CheckInterpolation(IPairWithInterpolation pair)
        {
            if (((Pair) pair).ValueQuotesType != 2) return;
            foreach (var item in pair.InterpolationItems)
            {
                var alias = item as Alias;
                if (alias != null)
                {
                    CheckAliasIsDefined(alias);
                    continue;
                }

                var param = item as DOM.Parameter;
                if (param != null)
                {
                    var aliasContext = AliasContext.Peek();
                    if (aliasContext != null)
                        ValidateParameter((Parameter) param, aliasContext.Alias, _currentModule.FileName);
                }
            }
        }

        /// <summary>
        /// Checks that XML element doesn't have empty name unless it is ValueNode inside the element (mixed content).
        /// </summary>
        /// <param name="node"></param>
        private void CheckArrayItem(Element node)
        {
            if (!string.IsNullOrEmpty(node.Name)) return; //not an array item
            if (_currentModule.TargetFormat != Module.TargetFormats.Xml) return; //don't check if s4j
            if (node.IsChoice) return; //don't check if node is choice
            if (node.Delimiter == DelimiterEnum.CC) return; //Empty name is allowed for choice containers
            if (node.Parent.Delimiter == DelimiterEnum.CCC) return; //Empty name if parent is an explicit array
            
            if (!node.IsValueNode || node.Parent is DOM.Document) //Empty name is allowed for value (text) nodes
            {
                Context.AddError(CompilerErrorFactory.InvalidXmlElementName(node.NameInterval, _currentModule.FileName));
            }
        }

        public override void OnAttribute(DOM.Attribute attribute)
        {
            CheckBlockIntegrity(attribute);
            CheckPairValue(attribute);
            if (CheckStartOfChoiceContainer(attribute, new PairCollection<Entity>().AddRange(((DOM.Mapped.Attribute)attribute).InterpolationItems?.OfType<Entity>())))
            {
                EndChoiceContainer();
            }
        }

        /// <summary>
        /// Ensures that pair value is either Alias or Parameter.
        /// Validates string concatenation.
        /// </summary>
        /// <param name="pair"></param>
        private void CheckPairValue(Pair pair)
        {
            if (pair.Delimiter == DelimiterEnum.CE)
            {
                if (pair.PairValue == null || !(pair.PairValue is DOM.Alias) && !(pair.PairValue is DOM.Parameter))
                {
                    var mappedPair = pair.PairValue as IMappedPair;
                    Context.AddError(mappedPair != null
                        ? CompilerErrorFactory.AliasOrParameterExpected(mappedPair.NameInterval, _currentModule.FileName)
                        : CompilerErrorFactory.AliasOrParameterExpected(((IMappedPair) pair).NameInterval,
                            _currentModule.FileName));
                }
                else
                {
                    Visit(pair.PairValue);
                }
            }
            else if (pair.Delimiter == DelimiterEnum.EC)
                CheckStringConcatenation(pair);
            if (pair.PairValue is DOM.Parameter)
            {
                var aliasContext = AliasContext.Peek();
                if (aliasContext != null)
                    ValidateParameter((Parameter)pair.PairValue, aliasContext.Alias, _currentModule.FileName);
            }
            else if (pair.PairValue is DOM.Alias)
            {
                Alias alias = (Alias) pair.PairValue;
                CheckAliasIsDefined(alias);

                var aliasDef = alias.AliasDefinition;
                //Do not resolve alias without AliasDef or having circular reference
                if (aliasDef == null || aliasDef.HasCircularReference) return;

                AliasContext.Push(new AliasContext() { AliasDefinition = aliasDef, Alias = alias, AliasNsInfo = GetContextNsInfo() });
                CheckForUnexpectedArguments(alias, aliasDef, _currentModule.FileName);
                CheckInterpolation(aliasDef);
                CheckPairValue(aliasDef);
                if (CheckStartOfChoiceContainer(alias, aliasDef.Entities, aliasDef))
                {
                    Visit(aliasDef.Entities.Where(e => !(e is DOM.Attribute)));
                    EndChoiceContainer(/*aliasDef, aliasDef.Entities*/);
                }
                else
                {
                    Visit(aliasDef.Entities.Where(e => !(e is DOM.Attribute)));
                }
                AliasContext.Pop();
            }
        }

        private void CheckStringConcatenation(Pair pair)
        {
            _blockState.Push(JsonGenerator.BlockState.Array);
            _blockStateUnknown = false;
            if (((IPairWithInterpolation)pair).InterpolationItems != null) 
                Visit(((IPairWithInterpolation) pair).InterpolationItems.ConvertAll(i => (Pair)i));
            _blockState.Pop();
        }

        private void CheckNoPairValue(Pair pair)
        {
            if (pair.Delimiter == DelimiterEnum.CE)
            {
               Context.AddError(CompilerErrorFactory.InvalidDelimiter(((IMappedPair)pair).NameInterval, _currentModule.FileName, ":="));
            }
        }

        public override void OnArgument(DOM.Argument argument)
        {
            CheckArgumentIntegrity((Argument) argument);
            CheckPairValue(argument);
            base.OnArgument(argument);
        }

        public override void OnParameter(DOM.Parameter parameter)
        {
            CheckPairValue(parameter);
            var aliasContext = AliasContext.Peek();
            if (aliasContext != null)
                ValidateParameter((Parameter) parameter, aliasContext.Alias, _currentModule.FileName);
            base.OnParameter(parameter);
        }


        private void ValidateParameter(Parameter parameter, Alias alias, string moduleFileName)
        {
            if (parameter.Name == "_") //Default parameter
            {
                if (!parameter.IsValueNode)
                {
                    if (alias.Entities.All(e => e is Comment) && alias.ValueType != ValueType.Object)
                        ReportErrorInsideChoice(() => CompilerErrorFactory.DefaultBlockArgumentIsMissing(alias,
                            moduleFileName));
                }
                else
                {
                    if (parameter.HasValue()) return; //if parameter has default value then skip check

                    if (!alias.HasValue())
                    {
                        ReportErrorInsideChoice(() => CompilerErrorFactory.DefaultValueArgumentIsMissing(alias,
                            moduleFileName));
                    }
                }
                return;
            }

            //Non default parameter
            var argument = alias.Arguments.FirstOrDefault(a => a.Name == parameter.Name);
            if (argument == null)
            {
                //Report Error if argument is missing and there is no default value for the parameter
                if (parameter.Value == null && parameter.Entities.Count == 0 && parameter.ValueType != ValueType.Object)
                    ReportErrorInsideChoice(() => CompilerErrorFactory.ArgumentIsMissing(alias, parameter.Name,
                        moduleFileName));
                return;
            }

            //Report error if type of argument (value/block) mismatch the type of parameter
            if (((Argument)argument).IsValueNode != parameter.IsValueNode)
            {
                ReportErrorInsideChoice(() => parameter.IsValueNode
                    ? CompilerErrorFactory.ValueArgumentIsExpected((Argument)argument,
                        moduleFileName)
                    : CompilerErrorFactory.BlockArgumentIsExpected((Argument)argument,
                        moduleFileName));
            }
        }


        public override void OnNamespaceDefinition(DOM.NamespaceDefinition namespaceDefinition)
        {
            CheckNoPairValue(namespaceDefinition);
            CheckNsDefDuplicates((NamespaceDefinition) namespaceDefinition);
            base.OnNamespaceDefinition(namespaceDefinition);
        }

        public override void OnScope(Scope pair)
        {
            CheckNoPairValue(pair);
            base.OnScope(pair);
        }

        private void CheckArgumentIntegrity(Argument argument)
        {
            if (! (argument.Parent is Alias))
                Context.AddError(CompilerErrorFactory.ArgumentMustBeDefinedInAlias(argument, _currentModule.FileName));
        }

        /// <summary>
        /// Sets state of the current block and checks its integrity
        /// </summary>
        /// <param name="pair"></param>
        private void CheckBlockIntegrity(DOM.Pair pair)
        {
            if (_currentModule.TargetFormat == Module.TargetFormats.Xml) return;
            if (!_blockStateUnknown)
            {
                var blockState = _blockState.Peek();
                if (blockState == JsonGenerator.BlockState.Array)
                {
                    if (string.IsNullOrEmpty(pair.Name) || pair.Delimiter == DelimiterEnum.None) return;
                    ReportErrorForEachNodeInAliasContext(
                        n => CompilerErrorFactory.ArrayItemIsExpected((IMappedPair) n, _currentModule.FileName));
                    Context.AddError(CompilerErrorFactory.ArrayItemIsExpected((IMappedPair) pair, _currentModule.FileName));
                }
                else
                {
                    if (!string.IsNullOrEmpty(pair.Name) && pair.Delimiter != DelimiterEnum.None) return;

                    if(_currentModule.TargetFormat == Module.TargetFormats.Xml && ((IMappedPair)pair).IsValueNode ) return; 

                    ReportErrorForEachNodeInAliasContext(
                        n => CompilerErrorFactory.PropertyIsExpected((IMappedPair) n, _currentModule.FileName));
                    Context.AddError(CompilerErrorFactory.PropertyIsExpected((IMappedPair) pair, _currentModule.FileName));
                }

                return;
            }

            //This element is the first element of the block. It decides if the block is array or object
            _blockState.Push(string.IsNullOrEmpty(pair.Name) || pair.Delimiter == DelimiterEnum.None
                ? JsonGenerator.BlockState.Array
                : JsonGenerator.BlockState.Object);
            _blockStateUnknown = false;
        }

        private void ReportErrorForEachNodeInAliasContext(Func<DOM.Pair, CompilerError> func)
        {
            foreach (var item in AliasContext)
            {
                if (item != null)
                {
                    Context.AddError(func(item.Alias));
                }
            }
        }


        private void CheckForUnexpectedArguments(Alias alias, AliasDefinition aliasDef, string fileName)
        {
            foreach (var argument in alias.Arguments)
            {
                if (aliasDef.Parameters.All(p => p.Name != argument.Name))
                    Context.AddError(CompilerErrorFactory.UnexpectedArgument((Argument)argument,
                        fileName));
            }

            if (!aliasDef.HasDefaultBlockParameter && alias.Entities.Any(e => !(e is DOM.Comment)))
            {
                Context.AddError(CompilerErrorFactory.UnexpectedDefaultBlockArgument((IMappedPair)alias.Entities[0],
                    fileName));
            }

            if (!aliasDef.HasDefaultValueParameter && alias.HasValue())
            {
                Context.AddError(CompilerErrorFactory.UnexpectedDefaultValueArgument(alias,
                    fileName));
            }
        }

        private void CheckCompatibilityWithParameters(Alias alias, AliasDefinition aliasDef, string fileName)
        {
            foreach (var parameter in aliasDef.Parameters)
            {
                if (parameter.Name == "_") //Default parameter
                {
                    if (!parameter.IsValueNode)
                    {
                        if (alias.Entities.All(e => e is Comment) && alias.ValueType != ValueType.Object)
                            ReportErrorInsideChoice(() => CompilerErrorFactory.DefaultBlockArgumentIsMissing(alias,
                                fileName));
                    }
                    else
                    {
                        if (parameter.HasValue()) continue; //if parameter has default value then skip check

                        if (!alias.HasValue())
                        {
                            ReportErrorInsideChoice(() => CompilerErrorFactory.DefaultValueArgumentIsMissing(alias,
                                fileName));
                        }
                    }
                    continue;
                }

                //Non default parameter
                var argument = alias.Arguments.FirstOrDefault(a => a.Name == parameter.Name);
                if (argument == null)
                {
                    //Report Error if argument is missing and there is no default value for the parameter
                    if (parameter.Value == null && parameter.Entities.Count == 0 && parameter.ValueType != ValueType.Object)
                        ReportErrorInsideChoice(() => CompilerErrorFactory.ArgumentIsMissing(alias, parameter.Name,
                            fileName));

                    continue;
                }

                //Report error if type of argument (value/block) mismatch the type of parameter
                if (((Argument)argument).IsValueNode != parameter.IsValueNode)
                {
                    ReportErrorInsideChoice( () => parameter.IsValueNode
                        ? CompilerErrorFactory.ValueArgumentIsExpected((Argument)argument,
                            fileName)
                        : CompilerErrorFactory.BlockArgumentIsExpected((Argument)argument,
                            fileName));
                }
            }
        }
    }
}
