﻿using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using DatabaseInterpreter.Model;
using SqlAnalyser.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using static PlSqlParser;

namespace SqlAnalyser.Core
{
    public class PlSqlRuleAnalyser : SqlRuleAnalyser
    {
        public override IEnumerable<Type> ParseTableTypes => new List<Type>() { typeof(Tableview_nameContext) };

        public override IEnumerable<Type> ParseColumnTypes => new List<Type>() { typeof(Variable_nameContext), typeof(Column_nameContext) };

        public override Lexer GetLexer(string content)
        {
            return new PlSqlLexer(this.GetCharStreamFromString(content));
        }

        public override Parser GetParser(CommonTokenStream tokenStream)
        {
            return new PlSqlParser(tokenStream);
        }

        public Sql_scriptContext GetRootContext(string content, out SqlSyntaxError error)
        {
            error = null;

            PlSqlParser parser = this.GetParser(content) as PlSqlParser;

            SqlSyntaxErrorListener errorListener = new SqlSyntaxErrorListener();

            parser.AddErrorListener(errorListener);

            Sql_scriptContext context = parser.sql_script();

            error = errorListener.Error;

            return context;
        }

        private bool CanIgnoreError(SqlSyntaxError error)
        {
            if (error == null)
            {
                return true;
            }

            if (error != null && error.Items.Count == 1)
            {
                return error.Items[0].Message == "mismatched input '<EOF>' expecting 'BEGIN'";
            }

            return false;
        }

        public Unit_statementContext GetUnitStatementContext(string content, out SqlSyntaxError error)
        {
            error = null;

            Sql_scriptContext rootContext = this.GetRootContext(content, out error);

            return rootContext?.unit_statement()?.FirstOrDefault();
        }

        public override AnalyseResult AnalyseCommon(string content)
        {
            SqlSyntaxError error = null;

            Sql_scriptContext rootContext = this.GetRootContext(content, out error);

            bool canIgnoreError = this.CanIgnoreError(error);

            if (canIgnoreError)
            {
                error = null;
            }

            AnalyseResult result = new AnalyseResult() { Error = error };

            if ((!result.HasError || canIgnoreError) && rootContext != null)
            {
                Unit_statementContext[] unitStatements = rootContext.unit_statement();

                if (unitStatements.Length > 0)
                {
                    CommonScript script = null;

                    var unitStatement = unitStatements.FirstOrDefault();

                    var proc = unitStatement.create_procedure_body();
                    var func = unitStatement.create_function_body();
                    var trigger = unitStatement.create_trigger();
                    var view = unitStatement.create_view();

                    if (proc != null)
                    {
                        script = new RoutineScript() { Type = RoutineType.PROCEDURE };

                        this.SetProcedureScript(script as RoutineScript, proc);
                    }
                    else if (func != null)
                    {
                        script = new RoutineScript() { Type = RoutineType.FUNCTION };

                        this.SetFunctionScript(script as RoutineScript, func);
                    }
                    else if (trigger != null)
                    {
                        script = new TriggerScript();

                        this.SetTriggerScript(script as TriggerScript, trigger);
                    }
                    else if (view != null)
                    {
                        script = new ViewScript();

                        this.SetViewScript(script as ViewScript, view);
                    }
                    else
                    {
                        script = new CommonScript();

                        foreach (var unit in unitStatements)
                        {
                            foreach (var child in unit.children)
                            {
                                if (child is Data_manipulation_language_statementsContext dmls)
                                {
                                    script.Statements.AddRange(this.ParseDataManipulationLanguageStatement(dmls));
                                }
                                else if (child is ParserRuleContext prc)
                                {
                                    script.Statements.AddRange(this.ParseStatement(prc));
                                }
                            }
                        }
                    }

                    result.Script = script;

                    this.ExtractFunctions(script, unitStatement);
                }
            }

            return result;
        }

        public override AnalyseResult AnalyseProcedure(string content)
        {
            SqlSyntaxError error = null;

            Unit_statementContext unitStatement = this.GetUnitStatementContext(content, out error);

            AnalyseResult result = new AnalyseResult() { Error = error };

            if (!result.HasError && unitStatement != null)
            {
                RoutineScript script = new RoutineScript() { Type = RoutineType.PROCEDURE };

                Create_procedure_bodyContext proc = unitStatement.create_procedure_body();

                if (proc != null)
                {
                    this.SetProcedureScript(script, proc);
                }

                this.ExtractFunctions(script, unitStatement);

                result.Script = script;
            }

            return result;
        }

        private void SetProcedureScript(RoutineScript script, Create_procedure_bodyContext proc)
        {
            #region Name
            Procedure_nameContext name = proc.procedure_name();

            if (name.id_expression() != null)
            {
                script.Schema = name.identifier().GetText();
                script.Name = new TokenInfo(name.id_expression());
            }
            else
            {
                script.Name = new TokenInfo(name.identifier());
            }
            #endregion

            #region Parameters   
            this.SetRoutineParameters(script, proc.parameter());
            #endregion

            #region Declare
            var declare = proc.seq_of_declare_specs();

            if (declare != null)
            {
                script.Statements.AddRange(declare.declare_spec().Select(item => this.ParseDeclareStatement(item)));
            }
            #endregion

            #region Body
            this.SetScriptBody(script, proc.body());
            #endregion
        }

        public override AnalyseResult AnalyseFunction(string content)
        {
            SqlSyntaxError error = null;

            Unit_statementContext unitStatement = this.GetUnitStatementContext(content, out error);

            AnalyseResult result = new AnalyseResult() { Error = error };

            if (!result.HasError && unitStatement != null)
            {
                RoutineScript script = new RoutineScript() { Type = RoutineType.FUNCTION };

                Create_function_bodyContext func = unitStatement.create_function_body();

                if (func != null)
                {
                    this.SetFunctionScript(script, func);
                }

                this.ExtractFunctions(script, unitStatement);

                result.Script = script;
            }

            return result;
        }

        private void SetFunctionScript(RoutineScript script, Create_function_bodyContext func)
        {
            #region Name
            Function_nameContext name = func.function_name();

            if (name.id_expression() != null)
            {
                script.Schema = name.identifier().GetText();
                script.Name = new TokenInfo(name.id_expression());
            }
            else
            {
                script.Name = new TokenInfo(name.identifier());
            }
            #endregion

            #region Parameters
            this.SetRoutineParameters(script, func.parameter());
            #endregion

            #region Declare
            var declare = func.seq_of_declare_specs();

            if (declare != null)
            {
                script.Statements.AddRange(declare.declare_spec().Select(item => this.ParseDeclareStatement(item)));
            }
            #endregion

            script.ReturnDataType = new TokenInfo(func.type_spec().GetText()) { Type = TokenType.DataType };

            #region Body
            this.SetScriptBody(script, func.body());
            #endregion
        }

        public override AnalyseResult AnalyseView(string content)
        {
            SqlSyntaxError error = null;

            Unit_statementContext unitStatement = this.GetUnitStatementContext(content, out error);

            AnalyseResult result = new AnalyseResult() { Error = error };

            if (!result.HasError && unitStatement != null)
            {
                ViewScript script = new ViewScript();

                Create_viewContext view = unitStatement.create_view();

                if (view != null)
                {
                    this.SetViewScript(script, view);
                }

                this.ExtractFunctions(script, unitStatement);

                result.Script = script;
            }

            return result;
        }

        private void SetViewScript(ViewScript script, Create_viewContext view)
        {
            #region Name
            Tableview_nameContext name = view.tableview_name();

            if (name.id_expression() != null)
            {
                script.Schema = name.identifier().GetText();
                script.Name = new TokenInfo(name.id_expression());
            }
            else
            {
                script.Name = new TokenInfo(name.identifier());
            }
            #endregion

            #region Statement

            foreach (var child in view.children)
            {
                if (child is Select_only_statementContext select)
                {
                    script.Statements.Add(this.ParseSelectOnlyStatement(select));
                }
            }

            #endregion
        }

        public override AnalyseResult AnalyseTrigger(string content)
        {
            SqlSyntaxError error = null;

            Unit_statementContext unitStatement = this.GetUnitStatementContext(content, out error);

            AnalyseResult result = new AnalyseResult() { Error = error };

            if (!result.HasError && unitStatement != null)
            {
                TriggerScript script = new TriggerScript();

                Create_triggerContext trigger = unitStatement.create_trigger();

                if (trigger != null)
                {
                    this.SetTriggerScript(script, trigger);
                }

                this.ExtractFunctions(script, unitStatement);

                result.Script = script;
            }

            return result;
        }

        private void SetTriggerScript(TriggerScript script, Create_triggerContext trigger)
        {
            #region Name

            Trigger_nameContext name = trigger.trigger_name();

            if (name.id_expression() != null)
            {
                script.Schema = name.identifier().GetText();
                script.Name = new TokenInfo(name.id_expression());
            }
            else
            {
                script.Name = new TokenInfo(name.identifier());
            }

            #endregion

            Simple_dml_triggerContext simpleDml = trigger.simple_dml_trigger();

            if (simpleDml != null)
            {
                Tableview_nameContext tableName = simpleDml.dml_event_clause().tableview_name();
                script.TableName = new TableName(tableName);

                Dml_event_elementContext[] events = simpleDml.dml_event_clause().dml_event_element();

                foreach (Dml_event_elementContext evt in events)
                {
                    TriggerEvent triggerEvent = (TriggerEvent)Enum.Parse(typeof(TriggerEvent), evt.GetText());

                    script.Events.Add(triggerEvent);
                }

                foreach (var child in trigger.children)
                {
                    if (child is TerminalNodeImpl terminalNode)
                    {
                        switch (terminalNode.Symbol.Type)
                        {
                            case PlSqlParser.BEFORE:
                                script.Time = TriggerTime.BEFORE;
                                break;
                            case PlSqlParser.AFTER:
                                script.Time = TriggerTime.AFTER;
                                break;
                            case PlSqlParser.INSTEAD:
                                script.Time = TriggerTime.INSTEAD_OF;
                                break;
                        }
                    }
                }
            }

            ConditionContext condition = trigger.trigger_when_clause()?.condition();

            if (condition != null)
            {
                script.Condition = new TokenInfo(condition) { Type = TokenType.TriggerCondition };
            }

            #region Body

            Trigger_bodyContext triggerBody = trigger.trigger_body();
            Trigger_blockContext block = triggerBody.trigger_block();

            Declare_specContext[] declares = block.declare_spec();

            if (declares != null && declares.Length > 0)
            {
                script.Statements.AddRange(declares.Select(item => this.ParseDeclareStatement(item)));
            }

            this.SetScriptBody(script, block.body());

            #endregion
        }

        public void SetScriptBody(CommonScript script, BodyContext body)
        {
            script.Statements.AddRange(this.ParseBody(body));
        }

        public List<Statement> ParseBody(BodyContext node)
        {
            List<Statement> statements = new List<Statement>();

            foreach (var child in node.children)
            {
                if (child is Seq_of_statementsContext seq)
                {
                    statements.AddRange(this.ParseSeqStatement(seq));
                }
            }

            if (node.exception_handler()?.Any() == true)
            {
                statements.Add(this.ParseException(node));
            }

            return statements;
        }

        public void SetRoutineParameters(RoutineScript script, ParameterContext[] parameters)
        {
            if (parameters != null)
            {
                foreach (ParameterContext parameter in parameters)
                {
                    Parameter parameterInfo = new Parameter();

                    Parameter_nameContext paraName = parameter.parameter_name();

                    parameterInfo.Name = new TokenInfo(paraName) { Type = TokenType.ParameterName };

                    parameterInfo.DataType = new TokenInfo(parameter.type_spec().GetText()) { Type = TokenType.DataType };

                    Default_value_partContext defaultValue = parameter.default_value_part();

                    if (defaultValue != null)
                    {
                        parameterInfo.DefaultValue = new TokenInfo(defaultValue);
                    }

                    this.SetParameterType(parameterInfo, parameter.children);

                    script.Parameters.Add(parameterInfo);
                }
            }
        }

        public List<Statement> ParseSeqStatement(Seq_of_statementsContext node)
        {
            List<Statement> statements = new List<Statement>();

            GotoStatement gotoStatement = null;

            foreach (var child in node.children)
            {
                if (child is StatementContext st)
                {
                    if (gotoStatement == null)
                    {
                        statements.AddRange(this.ParseStatement(st));
                    }
                    else
                    {
                        gotoStatement.Statements.AddRange(this.ParseStatement(st));
                    }
                }
                else if (child is Label_declarationContext labelDeclare)
                {
                    gotoStatement = this.ParseLabelDeclareStatement(labelDeclare);

                    statements.Add(gotoStatement);
                }
            }

            return statements;
        }

        public ExceptionStatement ParseException(BodyContext body)
        {
            ExceptionStatement statement = new ExceptionStatement();

            Exception_handlerContext[] handlers = body.exception_handler();

            if (handlers != null && handlers.Length > 0)
            {
                foreach (Exception_handlerContext handler in handlers)
                {
                    ExceptionItem exceptionItem = new ExceptionItem();
                    exceptionItem.Name = new TokenInfo(handler.exception_name().First());
                    exceptionItem.Statements.AddRange(this.ParseSeqStatement(handler.seq_of_statements()));

                    statement.Items.Add(exceptionItem);
                }
            }

            return statement;
        }

        public void SetParameterType(Parameter parameterInfo, IList<IParseTree> nodes)
        {
            foreach (var child in nodes)
            {
                if (child is TerminalNodeImpl terminalNode)
                {
                    if (terminalNode.Symbol.Type == PlSqlParser.IN)
                    {
                        parameterInfo.ParameterType = ParameterType.IN;
                    }
                    else if (terminalNode.Symbol.Type == PlSqlParser.OUT)
                    {
                        parameterInfo.ParameterType = ParameterType.OUT;
                    }
                    else if (terminalNode.Symbol.Type == PlSqlParser.INOUT)
                    {
                        parameterInfo.ParameterType = ParameterType.IN | ParameterType.OUT;
                    }
                }
            }
        }

        public List<Statement> ParseStatement(StatementContext node)
        {
            List<Statement> statements = new List<Statement>();

            foreach (var child in node.children)
            {
                statements.AddRange(this.ParseStatement(child));
            }

            return statements;
        }

        private List<Statement> ParseStatement(IParseTree node)
        {
            List<Statement> statements = new List<Statement>();

            Action<DatabaseObjectType, TokenType, ParserRuleContext> addDropStatement = (objType, tokenType, objName) =>
            {
                if (objName != null)
                {
                    DropStatement dropStatement = new DropStatement();
                    dropStatement.ObjectType = objType;
                    dropStatement.ObjectName = new NameToken(objName) { Type = tokenType };

                    statements.Add(dropStatement);
                }
            };

            if (node is Sql_statementContext sql)
            {
                statements.AddRange(this.ParseSqlStatement(sql));
            }
            else if (node is Assignment_statementContext assignment)
            {
                statements.AddRange(this.ParseSetStatement(assignment));
            }
            else if (node is If_statementContext @if)
            {
                statements.Add(this.ParseIfStatement(@if));
            }
            else if (node is Case_statementContext @case)
            {
                statements.Add(this.ParseCaseStatement(@case));
            }
            else if (node is Loop_statementContext loop)
            {
                statements.Add(this.ParseLoopStatement(loop));
            }
            else if (node is Function_callContext funcCall)
            {
                var statement = this.ParseFunctionCallStatement(funcCall);

                if (statement != null)
                {
                    statements.Add(statement);
                }
            }
            else if (node is Procedure_callContext procCall)
            {
                var statement = this.ParseProcedureCallStatement(procCall);

                if (statement != null)
                {
                    statements.Add(statement);
                }
            }
            else if (node is Exit_statementContext exit)
            {
                statements.Add(this.ParseExitStatement(exit));
            }
            else if (node is BodyContext body)
            {
                statements.AddRange(this.ParseBody(body));
            }
            else if (node is Return_statementContext @return)
            {
                statements.Add(this.ParseReturnStatement(@return));
            }
            else if (node is Create_tableContext create_table)
            {
                statements.Add(this.ParseCreateTableStatement(create_table));
            }
            else if (node is Truncate_tableContext truncate_Table)
            {
                statements.Add(this.ParseTruncateTableStatement(truncate_Table));
            }
            else if (node is Drop_tableContext drop_Table)
            {
                addDropStatement(DatabaseObjectType.Table, TokenType.TableName, drop_Table.tableview_name());
            }
            else if (node is Drop_viewContext drop_View)
            {
                addDropStatement(DatabaseObjectType.View, TokenType.ViewName, drop_View.tableview_name());
            }
            else if (node is Drop_typeContext drop_Type)
            {
                addDropStatement(DatabaseObjectType.Type, TokenType.TypeName, drop_Type.type_name());
            }
            else if (node is Drop_sequenceContext drop_Sequence)
            {
                addDropStatement(DatabaseObjectType.Sequence, TokenType.SequenceName, drop_Sequence.sequence_name());
            }
            else if (node is Drop_functionContext drop_Func)
            {
                addDropStatement(DatabaseObjectType.Function, TokenType.FunctionName, drop_Func.function_name());
            }
            else if (node is Drop_procedureContext drop_Proc)
            {
                addDropStatement(DatabaseObjectType.Procedure, TokenType.ProcedureName, drop_Proc.procedure_name());
            }
            else if (node is Drop_triggerContext drop_Trigger)
            {
                addDropStatement(DatabaseObjectType.Trigger, TokenType.TriggerName, drop_Trigger.trigger_name());
            }
            else if (node is Goto_statementContext gst)
            {
                statements.Add(this.ParseGotoStatement(gst));
            }
            else if (node is Anonymous_blockContext anonymous)
            {
                statements.AddRange(this.ParseAnonymousBlock(anonymous));
            }

            return statements;
        }

        public List<Statement> ParseAnonymousBlock(Anonymous_blockContext node)
        {
            List<Statement> statements = new List<Statement>();

            foreach (var child in node.children)
            {
                if (child is Seq_of_declare_specsContext sd)
                {
                    var declares = sd.declare_spec();

                    if (declares != null)
                    {
                        statements.AddRange(declares.Select(item=> this.ParseDeclareStatement(item)));
                    }
                }
                else if(child is Seq_of_statementsContext seq)
                {
                    statements.AddRange(this.ParseSeqStatement(seq));
                }
            }

            return statements;
        }

        public LoopExitStatement ParseExitStatement(Exit_statementContext node)
        {
            LoopExitStatement statement = new LoopExitStatement();

            string condition = node.condition().GetText();

            statement.Condition = new TokenInfo(condition) { Type = TokenType.ExitCondition };

            statement.IsCursorLoopExit = condition.Contains("%NOTFOUND");

            return statement;
        }

        public Statement ParseFunctionCallStatement(Function_callContext node)
        {
            Statement statement;

            var name = node.routine_name();
            var args = node.function_argument()?.argument();

            TokenInfo functionName = new TokenInfo(name) { Type = TokenType.FunctionName };

            string symbol = functionName.Symbol;

            if (symbol.IndexOf("DBMS_OUTPUT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                statement = new PrintStatement() { Content = new TokenInfo(node.function_argument()) };
            }
            else if (symbol == "RAISE_APPLICATION_ERROR")
            {
                statement = this.ParseRaiseErrorStatement(args);
            }
            else
            {
                statement = new CallStatement()
                {
                    Name = functionName,
                    Parameters = args?.Select(item => new Parameter() { Name = new TokenInfo(item) }).ToList()
                };
            }

            return statement;
        }

        public Statement ParseProcedureCallStatement(Procedure_callContext node)
        {
            Statement statement = null;

            var name = node.routine_name();
            var args = node.function_argument()?.argument();

            if (name.GetText() == "RAISE_APPLICATION_ERROR")
            {
                statement = this.ParseRaiseErrorStatement(args);
            }

            return statement;
        }

        private Statement ParseRaiseErrorStatement(ArgumentContext[] args)
        {
            RaiseErrorStatement statement = new RaiseErrorStatement();

            if (args != null && args.Length > 0)
            {
                statement.ErrorCode = new TokenInfo(args[0]);
                statement.Content = new TokenInfo(args[1]);
            }

            return statement;
        }

        public List<Statement> ParseSqlStatement(Sql_statementContext node)
        {
            List<Statement> statements = new List<Statement>();

            foreach (var child in node.children)
            {
                if (child is Data_manipulation_language_statementsContext data)
                {
                    statements.AddRange(this.ParseDataManipulationLanguageStatement(data));
                }
                else if (child is Cursor_manipulation_statementsContext cursor)
                {
                    statements.AddRange(this.ParseCursorManipulationtatement(cursor));
                }
            }

            return statements;
        }

        public List<Statement> ParseDataManipulationLanguageStatement(Data_manipulation_language_statementsContext node)
        {
            List<Statement> statements = new List<Statement>();

            foreach (var child in node.children)
            {
                if (child is Select_statementContext select)
                {
                    statements.Add(this.ParseSelectStatement(select));
                }
                else if (child is Insert_statementContext insert)
                {
                    statements.Add(this.ParseInsertStatement(insert));
                }
                else if (child is Update_statementContext update)
                {
                    statements.Add(this.ParseUpdateStatement(update));
                }
                else if (child is Delete_statementContext delete)
                {
                    statements.AddRange(this.ParseDeleteStatement(delete));
                }
            }

            return statements;
        }

        public List<Statement> ParseCursorManipulationtatement(Cursor_manipulation_statementsContext node)
        {
            List<Statement> statements = new List<Statement>();

            foreach (var child in node.children)
            {
                if (child is Open_statementContext open)
                {
                    statements.Add(this.ParseOpenCursorStatement(open));
                }
                else if (child is Fetch_statementContext fetch)
                {
                    statements.Add(this.ParseFetchCursorStatement(fetch));
                }
                else if (child is Close_statementContext close)
                {
                    statements.Add(this.ParseCloseCursorStatement(close));
                }
            }

            return statements;
        }

        public OpenCursorStatement ParseOpenCursorStatement(Open_statementContext node)
        {
            OpenCursorStatement statement = new OpenCursorStatement();

            statement.CursorName = new TokenInfo(node.cursor_name()) { Type = TokenType.CursorName };

            return statement;
        }

        public FetchCursorStatement ParseFetchCursorStatement(Fetch_statementContext node)
        {
            FetchCursorStatement statement = new FetchCursorStatement();

            statement.CursorName = new TokenInfo(node.cursor_name()) { Type = TokenType.CursorName };
            statement.Variables.AddRange(node.variable_name().Select(item => new TokenInfo(item) { Type = TokenType.VariableName }));

            return statement;
        }

        public CloseCursorStatement ParseCloseCursorStatement(Close_statementContext node)
        {
            CloseCursorStatement statement = new CloseCursorStatement() { IsEnd = true };

            statement.CursorName = new TokenInfo(node.cursor_name()) { Type = TokenType.CursorName };

            return statement;
        }

        public InsertStatement ParseInsertStatement(Insert_statementContext node)
        {
            InsertStatement statement = new InsertStatement();

            Single_table_insertContext single = node.single_table_insert();

            if (single != null)
            {
                foreach (var child in single.children)
                {
                    if (child is Insert_into_clauseContext into)
                    {
                        statement.TableName = this.ParseTableName(into.general_table_ref());

                        var columns = into.paren_column_list();

                        if (columns != null)
                        {
                            foreach (Column_nameContext colName in columns.column_list().column_name())
                            {
                                statement.Columns.Add(this.ParseColumnName(colName));
                            }
                        }
                    }
                    else if (child is Values_clauseContext values)
                    {
                        foreach (var v in values.children)
                        {
                            if (v is ExpressionsContext exp)
                            {
                                foreach (var expChild in exp.children)
                                {
                                    if (expChild is ExpressionContext value)
                                    {
                                        TokenInfo valueInfo = new TokenInfo(value) { Type = TokenType.InsertValue };

                                        statement.Values.Add(valueInfo);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return statement;
        }

        public UpdateStatement ParseUpdateStatement(Update_statementContext node)
        {
            UpdateStatement statement = new UpdateStatement();

            General_table_refContext table = node.general_table_ref();

            statement.TableNames.Add(this.ParseTableName(table));

            Update_set_clauseContext set = node.update_set_clause();
            Column_based_update_set_clauseContext[] columnSets = set.column_based_update_set_clause();

            if (columnSets != null)
            {
                foreach (Column_based_update_set_clauseContext colSet in columnSets)
                {
                    ColumnName columnName = null;

                    var col = colSet.column_name();

                    if (col != null)
                    {
                        columnName = this.ParseColumnName(col);
                    }
                    else
                    {
                        var col2 = colSet.paren_column_list()?.column_list();

                        if (col2 != null)
                        {
                            columnName = this.ParseColumnName(col2);

                            this.AddChildColumnNameToken(col2, columnName);
                        }
                    }

                    var valueExp = colSet.expression();
                    var subQuery = colSet.subquery();

                    TokenInfo value = null;

                    if (valueExp != null)
                    {
                        value = this.CreateToken(valueExp, TokenType.UpdateSetValue);

                        this.AddChildTableAndColumnNameToken(valueExp, value);
                    }
                    else if (subQuery != null)
                    {
                        value = this.CreateToken(subQuery, TokenType.UpdateSetValue);

                        this.AddChildTableAndColumnNameToken(subQuery, value);
                    }

                    statement.SetItems.Add(new NameValueItem()
                    {
                        Name = columnName,
                        Value = value
                    });
                }
            }

            var condition = node.where_clause();

            if (condition != null)
            {
                statement.Condition = this.ParseCondition(condition.expression());
            }

            return statement;
        }

        public List<DeleteStatement> ParseDeleteStatement(Delete_statementContext node)
        {
            List<DeleteStatement> statements = new List<DeleteStatement>();

            DeleteStatement statement = new DeleteStatement();
            statement.TableName = this.ParseTableName(node.general_table_ref());

            var condition = node.where_clause()?.expression();

            if (condition != null)
            {
                statement.Condition = this.ParseCondition(condition);
            }

            statements.Add(statement);

            return statements;
        }

        public SelectStatement ParseSelectStatement(Select_statementContext node)
        {
            SelectStatement statement = new SelectStatement();

            SelectLimitInfo selectLimitInfo = null;

            foreach (var child in node.children)
            {
                if (child is Select_only_statementContext query)
                {
                    statement = this.ParseSelectOnlyStatement(query);
                }
                else if (child is Offset_clauseContext offset)
                {
                    if (selectLimitInfo == null)
                    {
                        selectLimitInfo = new SelectLimitInfo();
                    }

                    selectLimitInfo.StartRowIndex = new TokenInfo(offset.expression());
                }
                else if (child is Fetch_clauseContext fetch)
                {
                    if (selectLimitInfo == null)
                    {
                        selectLimitInfo = new SelectLimitInfo();
                    }

                    selectLimitInfo.RowCount = new TokenInfo(fetch.expression());
                }
            }

            if (statement != null)
            {
                if (selectLimitInfo != null)
                {
                    statement.LimitInfo = selectLimitInfo;
                }
            }

            return statement;
        }

        public SelectStatement ParseSelectOnlyStatement(Select_only_statementContext node)
        {
            SelectStatement statement = new SelectStatement();

            List<WithStatement> withStatements = null;

            foreach (var child in node.children)
            {
                if (child is SubqueryContext subquery)
                {
                    statement = this.ParseSubQuery(subquery);
                }
                else if (child is Subquery_factoring_clauseContext factor)
                {
                    List<Statement> statements = this.ParseSubQueryFactoringCause(factor);

                    if (statements != null)
                    {
                        withStatements = statements.Where(item => item is WithStatement).Select(item => (WithStatement)item).ToList();
                    }
                }
            }

            if (withStatements != null)
            {
                statement.WithStatements = withStatements;
            }

            return statement;
        }

        public SelectStatement ParseSubQuery(SubqueryContext node)
        {
            SelectStatement statement = null;

            List<Statement> statements = new List<Statement>();

            foreach (var child in node.children)
            {
                if (child is Subquery_basic_elementsContext basic)
                {
                    statement = this.ParseSubQueryBasic(basic);
                }
                else if (child is Subquery_operation_partContext operation)
                {
                    Statement st = this.ParseSubQueryOperation(operation);

                    if (st != null)
                    {
                        statements.Add(st);
                    }
                }
            }

            if (statement != null)
            {
                var unionStatements = statements.Where(item => item is UnionStatement).Select(item => (UnionStatement)item);

                if (unionStatements.Count() > 0)
                {
                    statement.UnionStatements = unionStatements.ToList();
                }
            }

            return statement;
        }

        public List<Statement> ParseSubQueryFactoringCause(Subquery_factoring_clauseContext node)
        {
            List<Statement> statements = null;

            bool isWith = false;

            foreach (var fc in node.children)
            {
                if (fc is TerminalNodeImpl terminalNode)
                {
                    if (terminalNode.Symbol.Type == PlSqlParser.WITH)
                    {
                        isWith = true;
                    }
                }
                else if (fc is Factoring_elementContext fe)
                {
                    if (isWith)
                    {
                        if (statements == null)
                        {
                            statements = new List<Statement>();
                        }

                        WithStatement withStatement = new WithStatement() { SelectStatements = new List<SelectStatement>() };

                        withStatement.Name = new TableName(fe.query_name()) { Type = TokenType.General };

                        withStatement.SelectStatements.Add(this.ParseSubQuery(fe.subquery()));

                        statements.Add(withStatement);
                    }
                }
            }

            return statements;
        }

        public SelectStatement ParseSubQueryBasic(Subquery_basic_elementsContext node)
        {
            SelectStatement statement = new SelectStatement();

            Query_blockContext block = node.query_block();

            List<ColumnName> columnNames = new List<ColumnName>();

            Selected_listContext selectColumns = block.selected_list();

            foreach (Select_list_elementsContext col in selectColumns.select_list_elements())
            {
                columnNames.Add(this.ParseColumnName(col));
            }

            if (columnNames.Count == 0)
            {
                columnNames.Add(this.ParseColumnName(selectColumns));
            }

            statement.Columns = columnNames;

            statement.FromItems = this.ParseFromClause(block.from_clause());

            Into_clauseContext into = block.into_clause();

            if (into != null)
            {
                var variables = into.bind_variable();

                ParserRuleContext intoTable = null;

                if (variables.Length > 0)
                {
                    intoTable = variables.First();
                }
                else
                {
                    intoTable = into.general_element().First();
                }

                statement.IntoTableName = new TokenInfo(intoTable) { Type = TokenType.TableName };
            }

            Where_clauseContext where = block.where_clause();
            Order_by_clauseContext orderby = block.order_by_clause();
            Group_by_clauseContext groupby = block.group_by_clause();

            if (where != null)
            {
                statement.Where = this.ParseCondition(where.expression());
            }

            if (orderby != null)
            {
                Order_by_elementsContext[] orderbyElements = orderby.order_by_elements();

                if (orderbyElements != null && orderbyElements.Length > 0)
                {
                    statement.OrderBy = orderbyElements.Select(item => this.CreateToken(item, TokenType.OrderBy)).ToList();
                }
            }

            if (groupby != null)
            {
                Group_by_elementsContext[] groupbyElements = groupby.group_by_elements();
                Having_clauseContext having = groupby.having_clause();

                if (groupbyElements != null && groupbyElements.Length > 0)
                {
                    statement.GroupBy = groupbyElements.Select(item => this.CreateToken(item, TokenType.GroupBy)).ToList();
                }

                if (having != null)
                {
                    statement.Having = this.ParseCondition(having.condition());
                }
            }

            return statement;
        }

        public Statement ParseSubQueryOperation(Subquery_operation_partContext node)
        {
            Statement statement = null;

            bool isUnion = false;
            UnionType unionType = UnionType.UNION;

            foreach (var child in node.children)
            {
                if (child is TerminalNodeImpl terminalNode)
                {
                    int type = terminalNode.Symbol.Type;

                    switch (type)
                    {
                        case TSqlParser.UNION:
                            isUnion = true;
                            break;
                        case TSqlParser.ALL:
                            unionType = UnionType.UNION_ALL;
                            break;
                    }
                }
                else if (child is Subquery_basic_elementsContext basic)
                {
                    if (isUnion)
                    {
                        UnionStatement unionStatement = new UnionStatement();
                        unionStatement.Type = unionType;
                        unionStatement.SelectStatement = this.ParseSubQueryBasic(basic);

                        statement = unionStatement;
                    }
                }
            }

            return statement;
        }

        public List<FromItem> ParseFromClause(From_clauseContext node)
        {
            List<FromItem> fromItems = new List<FromItem>();

            Table_ref_listContext tableList = node.table_ref_list();
            Table_refContext[] tables = tableList.table_ref();

            bool asWhole = false;

            foreach (Table_refContext table in tables)
            {
                FromItem fromItem = new FromItem();

                fromItem.TableName = this.ParseTableName(table);

                Join_clauseContext[] joins = table.join_clause();
                Pivot_clauseContext pivot = table.pivot_clause();
                Unpivot_clauseContext unpivot = table.unpivot_clause();

                if (joins != null && joins.Length > 0)
                {
                    foreach (Join_clauseContext join in joins)
                    {
                        JoinItem joinItem = new JoinItem();

                        var joinType = join.outer_join_type();

                        string type = null;

                        bool matched = false;

                        JoinType jt = JoinType.INNER;

                        if (joinType != null)
                        {
                            type = joinType.GetText();

                            jt = this.GetJoinType(type, out matched);
                        }
                        else
                        {
                            foreach (var child in join.children)
                            {
                                if (child is TerminalNodeImpl tni)
                                {
                                    jt = this.GetJoinType(tni.GetText(), out matched);

                                    if (matched)
                                    {
                                        break;
                                    }
                                }
                            }
                        }

                        if (matched)
                        {
                            joinItem.Type = jt;
                            joinItem.TableName = this.ParseTableName(join.table_ref_aux());
                            joinItem.Condition = this.ParseCondition(join.join_on_part().FirstOrDefault()?.condition());

                            fromItem.JoinItems.Add(joinItem);
                        }
                        else
                        {
                            asWhole = true;
                            break;
                        }
                    }
                }
                else if (pivot != null)
                {
                    JoinItem joinItem = new JoinItem() { Type = JoinType.PIVOT };
                    joinItem.PivotItem = this.ParsePivot(pivot);
                    fromItem.JoinItems.Add(joinItem);
                }
                else if (unpivot != null)
                {
                    JoinItem joinItem = new JoinItem() { Type = JoinType.UNPIVOT };
                    joinItem.UnPivotItem = this.ParseUnPivot(unpivot);
                    fromItem.JoinItems.Add(joinItem);
                }

                fromItems.Add(fromItem);
            }

            if (asWhole)
            {
                fromItems.Clear();

                FromItem fromItem = new FromItem();
                fromItem.TableName = new TableName(tableList);

                this.AddChildTableAndColumnNameToken(tableList, fromItem.TableName);

                fromItems.Add(fromItem);
            }

            return fromItems;
        }

        private JoinType GetJoinType(string text, out bool matched)
        {
            matched = false;

            switch (text)
            {
                case nameof(PlSqlParser.INNER):
                    matched = true;
                    return JoinType.INNER;
                case nameof(PlSqlParser.LEFT):
                    matched = true;
                    return JoinType.LEFT;
                case nameof(PlSqlParser.RIGHT):
                    matched = true;
                    return JoinType.RIGHT;
                case nameof(PlSqlParser.FULL):
                    matched = true;
                    return JoinType.FULL;
                case nameof(PlSqlParser.CROSS):
                    matched = true;
                    return JoinType.CROSS;
                default:
                    matched = false;
                    return JoinType.INNER;
            }
        }

        public PivotItem ParsePivot(Pivot_clauseContext node)
        {
            PivotItem pivotItem = new PivotItem();

            Pivot_elementContext pm = node.pivot_element().FirstOrDefault();

            Aggregate_function_nameContext function = pm.aggregate_function_name();

            pivotItem.AggregationFunctionName = new TokenInfo(function.identifier());
            pivotItem.AggregatedColumnName = this.CreateToken(pm.expression(), TokenType.ColumnName);
            pivotItem.ColumnName = this.ParseColumnName(node.pivot_for_clause().column_name());
            pivotItem.Values = node.pivot_in_clause().pivot_in_clause_element().Select(item => new TokenInfo(item)).ToList();

            return pivotItem;
        }

        public UnPivotItem ParseUnPivot(Unpivot_clauseContext node)
        {
            UnPivotItem unpivotItem = new UnPivotItem();
            unpivotItem.ValueColumnName = this.ParseColumnName(node.column_name());
            unpivotItem.ForColumnName = this.ParseColumnName(node.pivot_for_clause().column_name());
            unpivotItem.InColumnNames = node.unpivot_in_clause().unpivot_in_elements().Select(item => this.ParseColumnName(item.column_name())).ToList();

            return unpivotItem;
        }

        public List<SetStatement> ParseSetStatement(Assignment_statementContext node)
        {
            List<SetStatement> statements = new List<SetStatement>();

            foreach (var child in node.children)
            {
                if (child is General_elementContext element)
                {
                    SetStatement statement = new SetStatement();

                    statement.Key = new TokenInfo(element);

                    statements.Add(statement);
                }
                else if (child is ExpressionContext exp)
                {
                    statements.Last().Value = this.CreateToken(exp);
                }
            }

            return statements;
        }

        public Statement ParseDeclareStatement(Declare_specContext node)
        {
            Statement statement = null;

            foreach (var child in node.children)
            {
                if (child is Variable_declarationContext variable)
                {
                    DeclareVariableStatement declareStatement = new DeclareVariableStatement();

                    declareStatement.Name = new TokenInfo(variable.identifier()) { Type = TokenType.VariableName };

                    var typeSpec = variable.type_spec();
                    declareStatement.DataType = new TokenInfo(typeSpec);

                    declareStatement.IsCopyingDataType = typeSpec.children.Any(item => item.GetText() == "%TYPE");

                    var expression = variable.default_value_part()?.expression();

                    if (expression != null)
                    {
                        declareStatement.DefaultValue = new TokenInfo(expression);
                    }

                    statement = declareStatement;
                }
                else if (child is Cursor_declarationContext cursor)
                {
                    DeclareCursorStatement declareCursorStatement = new DeclareCursorStatement();

                    declareCursorStatement.CursorName = new TokenInfo(cursor.identifier()) { Type = TokenType.CursorName };
                    declareCursorStatement.SelectStatement = this.ParseSelectStatement(cursor.select_statement());

                    statement = declareCursorStatement;
                }
            }

            return statement;
        }

        public IfStatement ParseIfStatement(If_statementContext node)
        {
            IfStatement statement = new IfStatement();

            IfStatementItem ifItem = new IfStatementItem() { Type = IfStatementType.IF };
            ifItem.Condition = new TokenInfo(node.condition()) { Type = TokenType.IfCondition };
            ifItem.Statements.AddRange(this.ParseSeqStatement(node.seq_of_statements()));
            statement.Items.Add(ifItem);

            foreach (Elsif_partContext elseif in node.elsif_part())
            {
                IfStatementItem elseIfItem = new IfStatementItem() { Type = IfStatementType.ELSEIF };
                elseIfItem.Condition = new TokenInfo(elseif.condition()) { Type = TokenType.IfCondition };
                elseIfItem.Statements.AddRange(this.ParseSeqStatement(elseif.seq_of_statements()));

                statement.Items.Add(elseIfItem);
            }

            Else_partContext @else = node.else_part();
            if (@else != null)
            {
                IfStatementItem elseItem = new IfStatementItem() { Type = IfStatementType.ELSE };
                elseItem.Statements.AddRange(this.ParseSeqStatement(@else.seq_of_statements()));

                statement.Items.Add(elseItem);
            }

            return statement;
        }

        public CaseStatement ParseCaseStatement(Case_statementContext node)
        {
            CaseStatement statement = new CaseStatement();

            Simple_case_statementContext simple = node.simple_case_statement();

            if (simple != null)
            {
                statement.VariableName = new TokenInfo(simple.expression()) { Type = TokenType.VariableName };

                Simple_case_when_partContext[] whens = simple.simple_case_when_part();

                foreach (Simple_case_when_partContext when in whens)
                {
                    IfStatementItem ifItem = new IfStatementItem() { Type = IfStatementType.IF };
                    ifItem.Condition = new TokenInfo(when.expression().First()) { Type = TokenType.IfCondition };
                    ifItem.Statements.AddRange(this.ParseSeqStatement(when.seq_of_statements()));
                    statement.Items.Add(ifItem);
                }

                Case_else_partContext @else = simple.case_else_part();

                if (@else != null)
                {
                    IfStatementItem elseItem = new IfStatementItem() { Type = IfStatementType.ELSE };
                    elseItem.Statements.AddRange(this.ParseSeqStatement(@else.seq_of_statements()));

                    statement.Items.Add(elseItem);
                }
            }

            return statement;
        }

        public LoopStatement ParseLoopStatement(Loop_statementContext node)
        {
            LoopStatement statement = new LoopStatement();

            int i = 0;

            foreach (var child in node.children)
            {
                if (child is TerminalNodeImpl terminalNode)
                {
                    if (i == 0)
                    {
                        int type = terminalNode.Symbol.Type;

                        if (type == PlSqlParser.FOR)
                        {
                            statement.Type = LoopType.FOR;
                        }
                        else if (type == PlSqlParser.WHILE)
                        {
                            statement.Type = LoopType.WHILE;
                        }
                        else if (type == PlSqlParser.LOOP)
                        {
                            statement.Type = LoopType.LOOP;
                        }
                    }
                }
                else if (child is Seq_of_statementsContext seq)
                {
                    statement.Statements.AddRange(this.ParseSeqStatement(seq));
                }
                else if (child is ConditionContext condition)
                {
                    statement.Condition = new TokenInfo(condition) { Type = TokenType.IfCondition };
                }
                else if (child is Cursor_loop_paramContext cursor)
                {
                    statement.Condition = new TokenInfo(cursor) { Type = TokenType.IfCondition };
                }

                i++;
            }

            return statement;
        }

        public Statement ParseReturnStatement(Return_statementContext node)
        {
            Statement statement = new ReturnStatement();

            var expressioin = node.expression();

            if (expressioin != null)
            {
                statement = new ReturnStatement() { Value = new TokenInfo(expressioin) };
            }
            else
            {
                statement = new LeaveStatement() { Content = new TokenInfo(node) };
            }

            return statement;
        }

        public TransactionStatement ParseTransactionStatement(Transaction_control_statementsContext node)
        {
            TransactionStatement statement = new TransactionStatement();
            statement.Content = new TokenInfo(node);

            if (node.set_transaction_command() != null)
            {
                statement.CommandType = TransactionCommandType.SET;
            }
            else if (node.commit_statement() != null)
            {
                statement.CommandType = TransactionCommandType.COMMIT;
            }
            else if (node.rollback_statement() != null)
            {
                statement.CommandType = TransactionCommandType.ROLLBACK;
            }

            return statement;
        }

        public GotoStatement ParseLabelDeclareStatement(Label_declarationContext node)
        {
            GotoStatement statement = new GotoStatement();

            statement.Label = new TokenInfo(node.label_name());

            return statement;
        }

        public GotoStatement ParseGotoStatement(Goto_statementContext node)
        {
            GotoStatement statement = new GotoStatement();

            statement.Label = new TokenInfo(node.label_name());

            return statement;
        }

        public CreateTableStatement ParseCreateTableStatement(Create_tableContext node)
        {
            CreateTableStatement statement = new CreateTableStatement();

            TableInfo tableInfo = new TableInfo();

            foreach (var child in node.children)
            {
                if (child is TerminalNodeImpl tni)
                {
                    string text = tni.GetText();

                    if (text == "TEMPORARY")
                    {
                        tableInfo.IsTemporary = true;
                    }
                    else if (text == "PRIVATE")
                    {
                        tableInfo.IsGlobal = false;
                    }
                }
            }

            tableInfo.Name = this.ParseTableName(node.tableview_name());

            var columns = node.relational_table().relational_property();

            tableInfo.Columns.AddRange(columns.Select(item => new ColumnInfo()
            {
                Name = this.ParseColumnName(item.column_definition().column_name()),
                DataType = new TokenInfo(item.column_definition().datatype())
            }));

            statement.TableInfo = tableInfo;

            return statement;
        }

        public TruncateStatement ParseTruncateTableStatement(Truncate_tableContext node)
        {
            TruncateStatement statement = new TruncateStatement();

            statement.TableName = this.ParseTableName(node.tableview_name());

            return statement;
        }

        public override TableName ParseTableName(ParserRuleContext node, bool strict = false)
        {
            TableName tableName = null;

            Action<Table_aliasContext> setAlias = (alias) =>
            {
                if (tableName != null && alias != null)
                {
                    tableName.HasAs = this.HasAsFlag(alias);
                    tableName.Alias = new TokenInfo(alias);
                }
            };

            if (node != null)
            {
                if (node is Tableview_nameContext tv)
                {
                    tableName = new TableName(tv);

                    var parent = tv.Parent?.Parent?.Parent;

                    if (parent != null && parent is Table_ref_auxContext tra)
                    {
                        var alias = tra.table_alias();

                        if (alias != null)
                        {
                            tableName.Alias = new TokenInfo(alias);
                        }
                    }
                }
                else if (node is Table_ref_aux_internal_oneContext traio)
                {
                    tableName = new TableName(traio);

                    var parent = traio.Parent;

                    if (parent != null && parent is Table_ref_auxContext trau)
                    {
                        var alias = trau.table_alias();

                        if (alias != null)
                        {
                            tableName.Alias = new TokenInfo(alias);
                        }
                    }
                }
                else if (node is General_table_refContext gtr)
                {
                    tableName = new TableName(gtr.dml_table_expression_clause().tableview_name());

                    setAlias(gtr.table_alias());
                }
                else if (node is Table_ref_auxContext tra)
                {
                    var tfa = tra.table_ref_aux_internal();

                    tableName = new TableName(tfa);

                    setAlias(tra.table_alias());

                    if (AnalyserHelper.IsSubquery(tfa))
                    {
                        this.AddChildTableAndColumnNameToken(tfa, tableName);
                    }
                }
                else if (node is Table_ref_listContext trl)
                {
                    return this.ParseTableName(trl.table_ref().FirstOrDefault());
                }
                else if (node is Table_refContext tr)
                {
                    return this.ParseTableName(tr.table_ref_aux());
                }

                if (!strict && tableName == null)
                {
                    tableName = new TableName(node);
                }
            }

            return tableName;
        }

        public override ColumnName ParseColumnName(ParserRuleContext node, bool strict = false)
        {
            ColumnName columnName = null;

            if (node != null)
            {
                if (node is Column_nameContext cn)
                {
                    columnName = new ColumnName(cn);
                }
                else if (node is Variable_nameContext vname)
                {
                    columnName = new ColumnName(vname);

                    var ids = vname.id_expression();

                    if (ids.Length > 1)
                    {
                        columnName.TableName = new TableName(ids[0]);
                    }

                    var sle = this.FindSelectListEelementsContext(vname);

                    if (sle != null)
                    {
                        var alias = sle.column_alias()?.identifier();

                        if (alias != null)
                        {
                            columnName.Alias = new TokenInfo(alias);
                        }
                    }
                }
                else if (node is Select_list_elementsContext ele)
                {
                    columnName = null;

                    Tableview_nameContext tableName = ele.tableview_name();
                    ExpressionContext expression = ele.expression();
                    Column_aliasContext alias = ele.column_alias();

                    if (expression != null)
                    {
                        columnName = new ColumnName(expression);

                        if (this.HasFunction(expression))
                        {
                            this.AddChildColumnNameToken(expression, columnName);
                        }
                    }
                    else
                    {
                        columnName = new ColumnName(ele);
                    }

                    if (tableName != null)
                    {
                        columnName.TableName = new TokenInfo(tableName);
                    }

                    if (alias != null)
                    {
                        columnName.HasAs = this.HasAsFlag(alias);
                        columnName.Alias = new TokenInfo(alias.identifier());
                    }
                }
                else if (node is General_element_partContext gele)
                {
                    if (this.IsChildOfType<Select_list_elementsContext>(gele))
                    {
                        Id_expressionContext[] ids = gele.id_expression();

                        if (ids != null && ids.Length > 0)
                        {
                            if (ids.Length > 1)
                            {
                                columnName = new ColumnName(ids[1]);
                            }
                            else
                            {
                                columnName = new ColumnName(ids[0]);
                            }
                        }
                    }
                }

                if (!strict && columnName == null)
                {
                    columnName = new ColumnName(node);
                }
            }

            return columnName;
        }

        private Select_list_elementsContext FindSelectListEelementsContext(ParserRuleContext node)
        {
            if (node != null)
            {
                if (node.Parent != null && node.Parent is Select_list_elementsContext sle)
                {
                    return sle;
                }
                else if (node.Parent != null && node.Parent is ParserRuleContext)
                {
                    return this.FindSelectListEelementsContext(node.Parent as ParserRuleContext);
                }
            }

            return null;
        }

        private TokenInfo ParseCondition(ParserRuleContext node)
        {
            if (node != null)
            {
                if (node is ConditionContext ||
                    node is Where_clauseContext ||
                    node is ExpressionContext)
                {
                    TokenInfo token = this.CreateToken(node);

                    bool isIfCondition = node.Parent != null && (node.Parent is If_statementContext || node.Parent is Loop_statementContext);

                    token.Type = isIfCondition ? TokenType.IfCondition : TokenType.SearchCondition;

                    if (!isIfCondition)
                    {
                        this.AddChildTableAndColumnNameToken(node, token);
                    }

                    return token;
                }
            }

            return null;
        }

        public override bool IsFunction(IParseTree node)
        {
            if (node is Standard_functionContext)
            {
                return true;
            }
            else if (node is General_element_partContext && (node as General_element_partContext).children.Any(item => item is Function_argumentContext))
            {
                return true;
            }
            else if (node is General_element_partContext gep)
            {
                if (gep.id_expression().Last().GetText() == "NEXTVAL")
                {
                    return true;
                }
            }
            else if (node is Variable_nameContext vn)
            {
                string text = vn.id_expression().Last().GetText();

                if (text == "NEXTVAL")
                {
                    return true;
                }
            }
            else if (node is Non_reserved_keywords_pre12cContext)
            {
                var parent = node.Parent?.Parent?.Parent;

                if (parent != null && parent is Variable_nameContext)
                {
                    return true;
                }
            }

            return false;
        }

        public override TokenInfo ParseFunction(ParserRuleContext node)
        {
            TokenInfo token = base.ParseFunction(node);

            if (node is General_element_partContext || node is Variable_nameContext)
            {
                Id_expressionContext[] ids = null;

                if (node is General_element_partContext gep)
                {
                    ids = gep.id_expression();
                }
                else if (node is Variable_nameContext vn)
                {
                    ids = vn.id_expression();
                }

                if (ids.Last().GetText() == "NEXTVAL")
                {
                    NameToken seqName;

                    if (ids.Length == 3)
                    {
                        seqName = new NameToken(ids[1]) { Type = TokenType.SequenceName };
                        seqName.Schema = ids[0].GetText();
                    }
                    else
                    {
                        seqName = new NameToken(ids[0]) { Type = TokenType.SequenceName };
                    }

                    token.AddChild(seqName);
                }
            }

            return token;
        }

        public bool HasFunction(ParserRuleContext node)
        {
            if (node == null)
            {
                return false;
            }

            foreach (var child in node.children)
            {
                if (this.IsFunction(child))
                {
                    return true;
                }
                else
                {
                    return this.HasFunction(child as ParserRuleContext);
                }
            }

            return false;
        }

        private bool IsChildOfType<T>(RuleContext node)
        {
            if (node == null || node.Parent == null)
            {
                return false;
            }

            if (node.Parent != null && node.Parent.GetType() == typeof(T))
            {
                return true;
            }
            else
            {
                return this.IsChildOfType<T>(node.Parent as RuleContext);
            }
        }
    }
}



