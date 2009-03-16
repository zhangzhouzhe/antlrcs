﻿/*
 * [The "BSD licence"]
 * Copyright (c) 2005-2008 Terence Parr
 * All rights reserved.
 *
 * Conversion to C#:
 * Copyright (c) 2008-2009 Sam Harwell, Pixel Mine, Inc.
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 * 1. Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * 2. Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 * 3. The name of the author may not be used to endorse or promote products
 *    derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
 * IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
 * IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT,
 * INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
 * DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
 * THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
 * THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

namespace Antlr3.Tool
{
    using System.Collections.Generic;
    using System.Linq;
    using Antlr.Runtime.JavaExtensions;

    using ANTLRParser = Antlr3.Grammars.ANTLRParser;
    using CodeGenerator = Antlr3.Codegen.CodeGenerator;
    using CommonToken = Antlr.Runtime.CommonToken;
    using IDictionary = System.Collections.IDictionary;
    using IList = System.Collections.IList;
    using IToken = Antlr.Runtime.IToken;
    using LookaheadSet = Antlr3.Analysis.LookaheadSet;
    using NFAState = Antlr3.Analysis.NFAState;

    /** Combine the info associated with a rule. */
    public class Rule
    {
        public string name;
        public int index;
        public string modifier;
        public NFAState startState;
        public NFAState stopState;

        /** This rule's options */
        protected IDictionary options;

        public static readonly HashSet<string> legalOptions = new HashSet<string>()
            {
                "k",
                "greedy",
                "memoize",
                "backtrack"
            };

        /** The AST representing the whole rule */
        public GrammarAST tree;

        /** To which grammar does this belong? */
        public Grammar grammar;

        /** For convenience, track the argument def AST action node if any */
        public GrammarAST argActionAST;

        public GrammarAST EORNode;

        /** The set of all tokens reachable from the start state w/o leaving
         *  via the accept state.  If it reaches the accept state, FIRST
         *  includes EOR_TOKEN_TYPE.
         */
        public LookaheadSet FIRST;

        /** The return values of a rule and predefined rule attributes */
        public AttributeScope returnScope;

        public AttributeScope parameterScope;

        /** the attributes defined with "scope {...}" inside a rule */
        public AttributeScope ruleScope;

        /** A list of scope names used by this rule */
        public List<string> useScopes;

        /** Exceptions that this rule can throw */
        public HashSet<string> throwsSpec;

        /** A list of all LabelElementPair attached to tokens like id=ID */
        public Dictionary<string, Grammar.LabelElementPair> tokenLabels;

        /** A list of all LabelElementPair attached to tokens like x=. in tree grammar */
        public Dictionary<string, Grammar.LabelElementPair> wildcardTreeLabels;

        /** A list of all LabelElementPair attached to tokens like x+=. in tree grammar */
        public Dictionary<string, Grammar.LabelElementPair> wildcardTreeListLabels;

        /** A list of all LabelElementPair attached to single char literals like x='a' */
        public Dictionary<string, Grammar.LabelElementPair> charLabels;

        /** A list of all LabelElementPair attached to rule references like f=field */
        public Dictionary<string, Grammar.LabelElementPair> ruleLabels;

        /** A list of all Token list LabelElementPair like ids+=ID */
        public Dictionary<string, Grammar.LabelElementPair> tokenListLabels;

        /** A list of all rule ref list LabelElementPair like ids+=expr */
        public Dictionary<string, Grammar.LabelElementPair> ruleListLabels;

        /** All labels go in here (plus being split per the above lists) to
         *  catch dup label and label type mismatches.
         */
        protected internal IDictionary<string, Grammar.LabelElementPair> labelNameSpace =
            new Dictionary<string, Grammar.LabelElementPair>();

        /** Map a name to an action for this rule.  Currently init is only
         *  one we use, but we can add more in future.
         *  The code generator will use this to fill holes in the rule template.
         *  I track the AST node for the action in case I need the line number
         *  for errors.  A better name is probably namedActions, but I don't
         *  want everyone to have to change their code gen templates now.
         */
        IDictionary<string, object> actions = new Dictionary<string, object>();

        /** Track all executable actions other than named actions like @init.
         *  Also tracks exception handlers, predicates, and rewrite rewrites.
         *  We need to examine these actions before code generation so
         *  that we can detect refs to $rule.attr etc...
         */
        IList<GrammarAST> inlineActions = new List<GrammarAST>();

        public int numberOfAlts;

        /** Each alt has a Map<tokenRefName,List<tokenRefAST>>; range 1..numberOfAlts.
         *  So, if there are 3 ID refs in a rule's alt number 2, you'll have
         *  altToTokenRef[2].get("ID").size()==3.  This is used to see if $ID is ok.
         *  There must be only one ID reference in the alt for $ID to be ok in
         *  an action--must be unique.
         *
         *  This also tracks '+' and "int" literal token references
         *  (if not in LEXER).
         *
         *  Rewrite rules force tracking of all tokens.
         */
        IDictionary<string, IList<GrammarAST>>[] altToTokenRefMap;

        /** Each alt has a Map<ruleRefName,List<ruleRefAST>>; range 1..numberOfAlts
         *  So, if there are 3 expr refs in a rule's alt number 2, you'll have
         *  altToRuleRef[2].get("expr").size()==3.  This is used to see if $expr is ok.
         *  There must be only one expr reference in the alt for $expr to be ok in
         *  an action--must be unique.
         *
         *  Rewrite rules force tracking of all rule result ASTs. 1..n
         */
        IDictionary<string, IList<GrammarAST>>[] altToRuleRefMap;

        /** Track which alts have rewrite rules associated with them. 1..n */
        bool[] altsWithRewrites;

        /** Do not generate start, stop etc... in a return value struct unless
         *  somebody references $r.start somewhere.
         */
        public bool referencedPredefinedRuleAttributes = false;

        public bool isSynPred = false;

        public bool imported = false;

        public Rule( Grammar grammar,
                    string ruleName,
                    int ruleIndex,
                    int numberOfAlts )
        {
            this.name = ruleName;
            this.index = ruleIndex;
            this.numberOfAlts = numberOfAlts;
            this.grammar = grammar;
            throwsSpec = new HashSet<string>() { "RecognitionException" };
            altToTokenRefMap = new IDictionary<string, IList<GrammarAST>>[numberOfAlts + 1]; //new Map[numberOfAlts + 1];
            altToRuleRefMap = new IDictionary<string, IList<GrammarAST>>[numberOfAlts + 1]; //new Map[numberOfAlts + 1];
            altsWithRewrites = new bool[numberOfAlts + 1];
            for ( int alt = 1; alt <= numberOfAlts; alt++ )
            {
                altToTokenRefMap[alt] = new Dictionary<string, IList<GrammarAST>>();
                altToRuleRefMap[alt] = new Dictionary<string, IList<GrammarAST>>();
            }
        }

        #region Properties
        public IDictionary<string, object> Actions
        {
            get
            {
                return actions;
            }
        }
        public bool HasMultipleReturnValues
        {
            get
            {
                return getHasMultipleReturnValues();
            }
        }
        public bool HasReturnValue
        {
            get
            {
                return getHasReturnValue();
            }
        }
        public bool HasSingleReturnValue
        {
            get
            {
                return getHasSingleReturnValue();
            }
        }
        public ICollection<GrammarAST> InlineActions
        {
            get
            {
                return getInlineActions();
            }
        }
        public IDictionary RuleLabels
        {
            get
            {
                return getRuleLabels();
            }
        }
        public IDictionary RuleListLabels
        {
            get
            {
                return getRuleListLabels();
            }
        }
        public string SingleValueReturnName
        {
            get
            {
                return getSingleValueReturnName();
            }
        }
        public string SingleValueReturnType
        {
            get
            {
                return getSingleValueReturnType();
            }
        }
        #endregion

        public virtual void defineLabel( IToken label, GrammarAST elementRef, int type )
        {
            Grammar.LabelElementPair pair = new Grammar.LabelElementPair( grammar, label, elementRef );
            pair.type = type;
            labelNameSpace[label.Text] = pair;
            switch ( type )
            {
            case Grammar.TOKEN_LABEL:
                if ( tokenLabels == null )
                {
                    tokenLabels = new Dictionary<string, Grammar.LabelElementPair>();
                }
                //tokenLabels.put( label.getText(), pair );
                tokenLabels[label.Text] = pair;
                break;

            case Grammar.WILDCARD_TREE_LABEL:
                if ( wildcardTreeLabels == null )
                    wildcardTreeLabels = new Dictionary<string, Grammar.LabelElementPair>();
                wildcardTreeLabels[label.Text] = pair;
                break;

            case Grammar.WILDCARD_TREE_LIST_LABEL:
                if ( wildcardTreeListLabels == null )
                    wildcardTreeListLabels = new Dictionary<string, Grammar.LabelElementPair>();
                wildcardTreeListLabels[label.Text] = pair;
                break;

            case Grammar.RULE_LABEL:
                if ( ruleLabels == null )
                {
                    ruleLabels = new Dictionary<string, Grammar.LabelElementPair>();
                }
                //ruleLabels.put( label.getText(), pair );
                ruleLabels[label.Text] = pair;
                break;
            case Grammar.TOKEN_LIST_LABEL:
                if ( tokenListLabels == null )
                {
                    tokenListLabels = new Dictionary<string, Grammar.LabelElementPair>();
                }
                //tokenListLabels.put( label.getText(), pair );
                tokenListLabels[label.Text] = pair;
                break;
            case Grammar.RULE_LIST_LABEL:
                if ( ruleListLabels == null )
                {
                    ruleListLabels = new Dictionary<string, Grammar.LabelElementPair>();
                }
                //ruleListLabels.put( label.getText(), pair );
                ruleListLabels[label.Text] = pair;
                break;
            case Grammar.CHAR_LABEL:
                if ( charLabels == null )
                {
                    charLabels = new Dictionary<string, Grammar.LabelElementPair>();
                }
                //charLabels.put( label.getText(), pair );
                charLabels[label.Text] = pair;
                break;
            }
        }

        public virtual Grammar.LabelElementPair getLabel( string name )
        {
            return (Grammar.LabelElementPair)labelNameSpace.get( name );
        }

        public virtual Grammar.LabelElementPair getTokenLabel( string name )
        {
            Grammar.LabelElementPair pair = null;
            if ( tokenLabels != null )
            {
                return (Grammar.LabelElementPair)tokenLabels.get( name );
            }
            return pair;
        }

        public virtual IDictionary getRuleLabels()
        {
            return ruleLabels;
        }

        public virtual IDictionary getRuleListLabels()
        {
            return ruleListLabels;
        }

        public virtual Grammar.LabelElementPair getRuleLabel( string name )
        {
            Grammar.LabelElementPair pair = null;
            if ( ruleLabels != null )
            {
                return (Grammar.LabelElementPair)ruleLabels.get( name );
            }
            return pair;
        }

        public virtual Grammar.LabelElementPair getTokenListLabel( string name )
        {
            Grammar.LabelElementPair pair = null;
            if ( tokenListLabels != null )
            {
                return (Grammar.LabelElementPair)tokenListLabels.get( name );
            }
            return pair;
        }

        public virtual Grammar.LabelElementPair getRuleListLabel( string name )
        {
            Grammar.LabelElementPair pair = null;
            if ( ruleListLabels != null )
            {
                return (Grammar.LabelElementPair)ruleListLabels.get( name );
            }
            return pair;
        }

        /** Track a token ID or literal like '+' and "void" as having been referenced
         *  somewhere within the alts (not rewrite sections) of a rule.
         *
         *  This differs from Grammar.altReferencesTokenID(), which tracks all
         *  token IDs to check for token IDs without corresponding lexer rules.
         */
        public virtual void trackTokenReferenceInAlt( GrammarAST refAST, int outerAltNum )
        {
            IList<GrammarAST> refs = altToTokenRefMap[outerAltNum].get( refAST.Text );
            if ( refs == null )
            {
                refs = new List<GrammarAST>();
                altToTokenRefMap[outerAltNum][refAST.Text] = refs;
            }
            refs.Add( refAST );
        }

        public virtual IList getTokenRefsInAlt( string @ref, int outerAltNum )
        {
            if ( altToTokenRefMap[outerAltNum] != null )
            {
                IList tokenRefASTs = (IList)altToTokenRefMap[outerAltNum].get( @ref );
                return tokenRefASTs;
            }
            return null;
        }

        public virtual void trackRuleReferenceInAlt( GrammarAST refAST, int outerAltNum )
        {
            IList<GrammarAST> refs = altToRuleRefMap[outerAltNum].get( refAST.Text );
            if ( refs == null )
            {
                refs = new List<GrammarAST>();
                altToRuleRefMap[outerAltNum][refAST.Text] = refs;
            }
            refs.Add( refAST );
        }

        public virtual IList getRuleRefsInAlt( string @ref, int outerAltNum )
        {
            if ( altToRuleRefMap[outerAltNum] != null )
            {
                IList ruleRefASTs = (IList)altToRuleRefMap[outerAltNum].get( @ref );
                return ruleRefASTs;
            }
            return null;
        }

        public virtual ICollection<string> getTokenRefsInAlt( int altNum )
        {
            return altToTokenRefMap[altNum].Keys;
        }

        /** For use with rewrite rules, we must track all tokens matched on the
         *  left-hand-side; so we need Lists.  This is a unique list of all
         *  token types for which the rule needs a list of tokens.  This
         *  is called from the rule template not directly by the code generator.
         */
        public virtual ICollection<string> getAllTokenRefsInAltsWithRewrites()
        {
            string output = (string)grammar.getOption( "output" );
            ICollection<string> tokens = new HashSet<string>();
            if ( output == null || !output.Equals( "AST" ) )
            {
                // return nothing if not generating trees; i.e., don't do for templates
                return tokens;
            }
            for ( int i = 1; i <= numberOfAlts; i++ )
            {
                if ( altsWithRewrites[i] )
                {
                    foreach ( string tokenName in altToTokenRefMap[i].Keys )
                    {
                        // convert token name like ID to ID, "void" to 31
                        int ttype = grammar.getTokenType( tokenName );
                        string label = grammar.generator.getTokenTypeAsTargetLabel( ttype );
                        tokens.Add( label );
                    }
                }
            }
            return tokens;
        }

        public virtual ICollection<string> getRuleRefsInAlt( int outerAltNum )
        {
            return altToRuleRefMap[outerAltNum].Keys;
        }

        /** For use with rewrite rules, we must track all rule AST results on the
         *  left-hand-side; so we need Lists.  This is a unique list of all
         *  rule results for which the rule needs a list of results.
         */
        public virtual ICollection<string> getAllRuleRefsInAltsWithRewrites()
        {
            var rules = from i in Enumerable.Range( 1, numberOfAlts )
                        where altsWithRewrites[i]
                        select altToRuleRefMap[i].Keys;

            return new HashSet<string>( rules.SelectMany( r => r ) );
        }

        public virtual IList<GrammarAST> getInlineActions()
        {
            return inlineActions;
        }

        public virtual bool hasRewrite( int i )
        {
            if ( i >= altsWithRewrites.Length )
            {
                ErrorManager.internalError( "alt " + i + " exceeds number of " + name +
                                           "'s alts (" + altsWithRewrites.Length + ")" );
                return false;
            }
            return altsWithRewrites[i];
        }

        /** Track which rules have rewrite rules.  Pass in the ALT node
         *  for the alt so we can check for problems when output=template,
         *  rewrite=true, and grammar type is tree parser.
         */
        public virtual void trackAltsWithRewrites( GrammarAST altAST, int outerAltNum )
        {
            if ( grammar.type == Grammar.TREE_PARSER &&
                 grammar.BuildTemplate &&
                 grammar.getOption( "rewrite" ) != null &&
                 grammar.getOption( "rewrite" ).Equals( "true" )
                )
            {
                GrammarAST firstElementAST = (GrammarAST)altAST.GetChild( 0 );
                grammar.sanity.ensureAltIsSimpleNodeOrTree( altAST,
                                                           firstElementAST,
                                                           outerAltNum );
            }
            altsWithRewrites[outerAltNum] = true;
        }

        /** Return the scope containing name */
        public virtual AttributeScope getAttributeScope( string name )
        {
            AttributeScope scope = getLocalAttributeScope( name );
            if ( scope != null )
            {
                return scope;
            }
            if ( ruleScope != null && ruleScope.getAttribute( name ) != null )
            {
                scope = ruleScope;
            }
            return scope;
        }

        /** Get the arg, return value, or predefined property for this rule */
        public virtual AttributeScope getLocalAttributeScope( string name )
        {
            AttributeScope scope = null;
            if ( returnScope != null && returnScope.getAttribute( name ) != null )
            {
                scope = returnScope;
            }
            else if ( parameterScope != null && parameterScope.getAttribute( name ) != null )
            {
                scope = parameterScope;
            }
            else
            {
                AttributeScope rulePropertiesScope =
                    RuleLabelScope.grammarTypeToRulePropertiesScope[grammar.type];
                if ( rulePropertiesScope.getAttribute( name ) != null )
                {
                    scope = rulePropertiesScope;
                }
            }
            return scope;
        }

        /** For references to tokens rather than by label such as $ID, we
         *  need to get the existing label for the ID ref or create a new
         *  one.
         */
        public virtual string getElementLabel( string refdSymbol,
                                      int outerAltNum,
                                      CodeGenerator generator )
        {
            GrammarAST uniqueRefAST;
            if ( grammar.type != Grammar.LEXER &&
                 char.IsUpper( refdSymbol[0] ) )
            {
                // symbol is a token
                IList tokenRefs = getTokenRefsInAlt( refdSymbol, outerAltNum );
                uniqueRefAST = (GrammarAST)tokenRefs[0];
            }
            else
            {
                // symbol is a rule
                IList ruleRefs = getRuleRefsInAlt( refdSymbol, outerAltNum );
                uniqueRefAST = (GrammarAST)ruleRefs[0];
            }
            if ( uniqueRefAST.code == null )
            {
                // no code?  must not have gen'd yet; forward ref
                return null;
            }
            string labelName = null;
            string existingLabelName =
                (string)uniqueRefAST.code.getAttribute( "label" );
            // reuse any label or list label if it exists
            if ( existingLabelName != null )
            {
                labelName = existingLabelName;
            }
            else
            {
                // else create new label
                labelName = generator.createUniqueLabel( refdSymbol );
                CommonToken label = new CommonToken( ANTLRParser.ID, labelName );
                if ( grammar.type != Grammar.LEXER &&
                     char.IsUpper( refdSymbol[0] ) )
                {
                    grammar.defineTokenRefLabel( name, label, uniqueRefAST );
                }
                else
                {
                    grammar.defineRuleRefLabel( name, label, uniqueRefAST );
                }
                uniqueRefAST.code.setAttribute( "label", labelName );
            }
            return labelName;
        }

        /** If a rule has no user-defined return values and nobody references
         *  it's start/stop (predefined attributes), then there is no need to
         *  define a struct; otherwise for now we assume a struct.  A rule also
         *  has multiple return values if you are building trees or templates.
         */
        public virtual bool getHasMultipleReturnValues()
        {
            return
                referencedPredefinedRuleAttributes || grammar.BuildAST ||
                grammar.BuildTemplate ||
                ( returnScope != null && returnScope.attributes.Count > 1 );
        }

        public virtual bool getHasSingleReturnValue()
        {
            return
                !( referencedPredefinedRuleAttributes || grammar.BuildAST ||
                  grammar.BuildTemplate ) &&
                                           ( returnScope != null && returnScope.attributes.Count == 1 );
        }

        public virtual bool getHasReturnValue()
        {
            return
                referencedPredefinedRuleAttributes || grammar.BuildAST ||
                grammar.BuildTemplate ||
                ( returnScope != null && returnScope.attributes.Count > 0 );
        }

        public virtual string getSingleValueReturnType()
        {
            if ( returnScope != null && returnScope.attributes.Count == 1 )
            {
                return returnScope.attributes[0].Type;
                //ICollection<Attribute> retvalAttrs = returnScope.attributes.Values;
                //return retvalAttrs.First().Type;

                //object[] javaSucks = retvalAttrs.toArray();
                //return ( (Attribute)javaSucks[0] ).type;
            }
            return null;
        }

        public virtual string getSingleValueReturnName()
        {
            if ( returnScope != null && returnScope.attributes.Count == 1 )
            {
                return returnScope.attributes[0].Name;
                //ICollection<Attribute> retvalAttrs = returnScope.attributes.Values;
                //return retvalAttrs.First().Name;

                //object[] javaSucks = retvalAttrs.toArray();
                //return ( (Attribute)javaSucks[0] ).name;
            }
            return null;
        }

        /** Given @scope::name {action} define it for this grammar.  Later,
         *  the code generator will ask for the actions table.
         */
        public virtual void defineNamedAction( GrammarAST ampersandAST,
                                      GrammarAST nameAST,
                                      GrammarAST actionAST )
        {
            //JSystem.@out.println("rule @"+nameAST.getText()+"{"+actionAST.getText()+"}");
            string actionName = nameAST.Text;
            GrammarAST a = (GrammarAST)actions.get( actionName );
            if ( a != null )
            {
                ErrorManager.grammarError(
                    ErrorManager.MSG_ACTION_REDEFINITION, grammar,
                    nameAST.Token, nameAST.Text );
            }
            else
            {
                actions[actionName] = actionAST;
            }
        }

        public virtual void trackInlineAction( GrammarAST actionAST )
        {
            inlineActions.Add( actionAST );
        }

        /** Save the option key/value pair and process it; return the key
         *  or null if invalid option.
         */
        public virtual string setOption( string key, object value, IToken optionsStartToken )
        {
            if ( !legalOptions.Contains( key ) )
            {
                ErrorManager.grammarError( ErrorManager.MSG_ILLEGAL_OPTION,
                                          grammar,
                                          optionsStartToken,
                                          key );
                return null;
            }
            if ( options == null )
            {
                options = new Dictionary<object, object>();
            }
            if ( key.Equals( "memoize" ) && value.ToString().Equals( "true" ) )
            {
                grammar.atLeastOneRuleMemoizes = true;
            }
            if ( key == "backtrack" && value.ToString() == "true" )
            {
                grammar.composite.getRootGrammar().atLeastOneBacktrackOption = true;
            }
            if ( key.Equals( "k" ) )
            {
                grammar.numberOfManualLookaheadOptions++;
            }
            options[key] = value;
            return key;
        }

        public virtual void setOptions( IDictionary<string, object> options, IToken optionsStartToken )
        {
            if ( options == null )
            {
                this.options = null;
                return;
            }
            //Set keys = options.keySet();
            //for ( Iterator it = keys.iterator(); it.hasNext(); )
            //{
            //    String optionName = (String)it.next();
            //    object optionValue = options.get( optionName );
            //    String stored = setOption( optionName, optionValue, optionsStartToken );
            //    if ( stored == null )
            //    {
            //        it.remove();
            //    }
            //}
            foreach ( string optionName in options.Keys.ToArray() )
            {
                object optionValue = options.get( optionName );
                string stored = setOption( optionName, optionValue, optionsStartToken );
                if ( stored == null )
                    options.Remove( optionName );
            }
        }

        /** Used during grammar imports to see if sets of rules intersect... This
         *  method and hashCode use the String name as the key for Rule objects.
        public boolean equals(Object other) {
            return this.name.equals(((Rule)other).name);
        }
         */

        /** Used during grammar imports to see if sets of rules intersect...
        public int hashCode() {
            return name.hashCode();
        }
         * */

        public override string ToString()
        { // used for testing
            return "[" + grammar.name + "." + name + ",index=" + index + ",line=" + tree.Token.Line + "]";
        }
    }
}
