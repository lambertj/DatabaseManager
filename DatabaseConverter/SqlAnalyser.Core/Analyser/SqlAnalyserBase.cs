﻿using DatabaseInterpreter.Model;
using SqlAnalyser.Core.Model;
using SqlAnalyser.Model;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SqlAnalyser.Core
{
    public abstract class SqlAnalyserBase
    {
        private StatementScriptBuilder statementBuilder;

        public abstract DatabaseType DatabaseType { get; }
        public abstract SqlRuleAnalyser RuleAnalyser { get; }
        public StatementScriptBuilderOption ScriptBuilderOption { get; set; } = new StatementScriptBuilderOption();

        public abstract SqlSyntaxError Validate(string content);
        public abstract AnalyseResult AnalyseCommon(string content);
        public abstract AnalyseResult AnalyseView(string content);
        public abstract AnalyseResult AnalyseProcedure(string content);
        public abstract AnalyseResult AnalyseFunction(string content);
        public abstract AnalyseResult AnalyseTrigger(string content);

        public abstract ScriptBuildResult GenerateRoutineScripts(RoutineScript script);
        public abstract ScriptBuildResult GenearteViewScripts(ViewScript script);
        public abstract ScriptBuildResult GenearteTriggerScripts(TriggerScript script);

        public AnalyseResult Analyse<T>(string content) where T : DatabaseObject
        {
            AnalyseResult result = null;

            if (this.RuleAnalyser.Option.IsCommonScript)
            {
                result = this.AnalyseCommon(content);
            }
            else
            {
                if (typeof(T) == typeof(Procedure))
                {
                    result = this.AnalyseProcedure(content);
                }
                else if (typeof(T) == typeof(Function))
                {
                    result = this.AnalyseFunction(content);
                }
                else if (typeof(T) == typeof(View))
                {
                    result = this.AnalyseView(content);
                }
                else if (typeof(T) == typeof(TableTrigger))
                {
                    result = this.AnalyseTrigger(content);
                }
                else
                {
                    throw new NotSupportedException($"Not support analyse for type:{typeof(T).Name}");
                }
            }

            return result;
        }

        public StatementScriptBuilder StatementBuilder
        {
            get
            {
                if (this.statementBuilder == null)
                {
                    this.statementBuilder = this.GetStatementBuilder();
                }

                return this.statementBuilder;
            }
        }

        private StatementScriptBuilder GetStatementBuilder()
        {
            StatementScriptBuilder builder = null;

            if (this.DatabaseType == DatabaseType.SqlServer)
            {
                builder = new TSqlStatementScriptBuilder();
            }
            else if (this.DatabaseType == DatabaseType.MySql)
            {
                builder = new MySqlStatementScriptBuilder();
            }
            else if (this.DatabaseType == DatabaseType.Oracle)
            {
                builder = new PlSqlStatementScriptBuilder();
            }
            else if (this.DatabaseType == DatabaseType.Postgres)
            {
                builder = new PostgreSqlStatementScriptBuilder();
            }
            else
            {
                throw new NotSupportedException($"Not support buid statement for: {this.DatabaseType}");
            }

            builder.Option = this.ScriptBuilderOption;

            return builder;
        }

        public string BuildStatement(Statement statement, RoutineType routineType = RoutineType.UNKNOWN)
        {
            this.StatementBuilder.RoutineType = routineType;

            this.StatementBuilder.Clear();

            this.StatementBuilder.Build(statement);

            return this.StatementBuilder.ToString();
        }

        public virtual ScriptBuildResult GenerateScripts(CommonScript script)
        {
            ScriptBuildResult result;

            if (script is RoutineScript routineScript)
            {
                result = this.GenerateRoutineScripts(routineScript);
            }
            else if (script is ViewScript viewScript)
            {
                result = this.GenearteViewScripts(viewScript);
            }
            else if (script is TriggerScript triggerScript)
            {
                result = this.GenearteTriggerScripts(triggerScript);
            }
            else if (script is CommonScript commonScript)
            {
                result = this.GenerateCommonScripts(commonScript);
            }
            else
            {
                throw new NotSupportedException($"Not support generate scripts for type: {script.GetType()}.");
            }

            if (this.statementBuilder != null && this.statementBuilder.Replacements.Count > 0)
            {
                foreach (var kp in this.statementBuilder.Replacements)
                {
                    result.Script = AnalyserHelper.ReplaceSymbol(result.Script, kp.Key, kp.Value);
                }
            }

            return result;
        }

        protected virtual void PreHandleStatements(List<Statement> statements) { }  
        protected virtual void PostHandleStatements(StringBuilder sb) { }

        protected virtual ScriptBuildResult GenerateCommonScripts(CommonScript script)
        {
            this.PreHandleStatements(script.Statements);

            ScriptBuildResult result = new ScriptBuildResult();

            StringBuilder sb = new StringBuilder();            

            foreach (Statement statement in script.Statements)
            {
                sb.AppendLine(this.BuildStatement(statement));
            }

            this.PostHandleStatements(sb);

            result.Script = sb.ToString();

            return result;
        }
    }
}
