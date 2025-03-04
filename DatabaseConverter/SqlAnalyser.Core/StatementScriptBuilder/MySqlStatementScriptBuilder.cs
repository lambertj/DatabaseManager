﻿using DatabaseInterpreter.Model;
using DatabaseInterpreter.Utility;
using SqlAnalyser.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace SqlAnalyser.Core
{
    public class MySqlStatementScriptBuilder : StatementScriptBuilder
    {
        protected override void PreHandleStatements(List<Statement> statements)
        {
            base.PreHandleStatements(statements);

            MySqlAnalyserHelper.RearrangeStatements(statements);
        }

        public override StatementScriptBuilder Build(Statement statement, bool appendSeparator = true)
        {
            base.Build(statement, appendSeparator);

            if (statement is SelectStatement select)
            {
                this.BuildSelectStatement(select, appendSeparator);
            }
            else if (statement is UnionStatement union)
            {
                this.AppendLine(this.GetUnionTypeName(union.Type));
                this.Build(union.SelectStatement);
            }
            else if (statement is InsertStatement insert)
            {
                this.Append($"INSERT INTO {insert.TableName}");

                if (insert.Columns.Count > 0)
                {
                    this.AppendLine($"({string.Join(",", insert.Columns.Select(item => item))})");
                }

                if (insert.SelectStatements != null && insert.SelectStatements.Count > 0)
                {
                    this.AppendChildStatements(insert.SelectStatements, true);
                }
                else
                {
                    this.AppendLine($"VALUES({string.Join(",", insert.Values.Select(item => item))});");
                }
            }
            else if (statement is UpdateStatement update)
            {
                bool hasJoin = AnalyserHelper.IsFromItemsHaveJoin(update.FromItems);
                int fromItemsCount = update.FromItems == null ? 0 : update.FromItems.Count;

                bool isCompositeColumnName = StatementScriptBuilderHelper.IsCompositeUpdateSetColumnName(update);

                this.Append($"UPDATE");

                if (isCompositeColumnName)
                {
                    this.Append(StatementScriptBuilderHelper.ParseCompositeUpdateSet(this, update));

                    return this;
                }

                List<TableName> tableNames = new List<TableName>();

                if (fromItemsCount > 0 && update.FromItems.First().TableName != null)
                {
                    tableNames.Add(update.FromItems.First().TableName);
                }
                else if (update.TableNames.Count > 0)
                {
                    tableNames.AddRange(update.TableNames);
                }

                this.Append($" {string.Join(",", tableNames.Where(item => item != null).Select(item => item.NameWithAlias))}");

                if (!hasJoin)
                {
                    if (fromItemsCount > 0)
                    {
                        for (int i = 0; i < fromItemsCount; i++)
                        {
                            var fromItem = update.FromItems[i];
                            var tableName = fromItem.TableName;

                            if (tableName != null && !tableNames.Contains(tableName))
                            {
                                this.Append($",{tableName.NameWithAlias}");
                            }
                            else if (fromItem.SubSelectStatement != null)
                            {
                                string alias = fromItem.Alias == null ? "" : fromItem.Alias.Symbol;

                                this.Append(",");
                                this.AppendLine("(");
                                this.BuildSelectStatement(fromItem.SubSelectStatement, false);
                                this.AppendLine($") {alias}");
                            }
                        }
                    }
                }
                else
                {
                    int i = 0;

                    foreach (FromItem fromItem in update.FromItems)
                    {
                        if (fromItem.TableName != null && i > 0)
                        {
                            this.AppendLine($" {fromItem.TableName}");
                        }

                        foreach (JoinItem joinItem in fromItem.JoinItems)
                        {
                            string condition = joinItem.Condition == null ? "" : $" ON {joinItem.Condition}";

                            this.AppendLine($"{joinItem.Type} JOIN {this.GetNameWithAlias(joinItem.TableName)}{condition}");
                        }

                        i++;
                    }
                }

                this.AppendLine("SET");

                int k = 0;

                foreach (var item in update.SetItems)
                {
                    this.Append($"{item.Name}=");

                    this.BuildUpdateSetValue(item);

                    if (k < update.SetItems.Count - 1)
                    {
                        this.Append(",");
                    }

                    this.AppendLine(this.Indent);

                    k++;
                }               

                if (update.Condition != null && update.Condition.Symbol != null)
                {
                    this.AppendLine($"WHERE {update.Condition}");
                }

                this.AppendLine(";");
            }
            else if (statement is DeleteStatement delete)
            {
                bool hasJoin = AnalyserHelper.IsFromItemsHaveJoin(delete.FromItems);

                if (!hasJoin)
                {
                    this.AppendLine($"DELETE FROM {this.GetNameWithAlias(delete.TableName)}");
                }
                else
                {
                    //"delete from join: from table can't use alias.

                    string tableName = delete.TableName.Symbol;

                    this.AppendLine($"DELETE {delete.TableName}");

                    string alias = null;

                    int i = 0;

                    foreach (FromItem fromItem in delete.FromItems)
                    {
                        if (i == 0)
                        {
                            if (fromItem.TableName != null && fromItem.TableName.Alias != null)
                            {
                                alias = fromItem.TableName.Alias.Symbol;
                            }
                            else if (fromItem.Alias != null)
                            {
                                alias = fromItem.Alias.Symbol;
                            }
                        }

                        foreach (JoinItem joinItem in fromItem.JoinItems)
                        {
                            string condition = joinItem.Condition.Symbol;

                            if (!string.IsNullOrEmpty(alias) && condition.Contains(alias))
                            {
                                joinItem.Condition.Symbol = AnalyserHelper.ReplaceSymbol(condition, alias, tableName);
                            }
                        }

                        i++;
                    }

                    this.BuildFromItems(delete.FromItems);

                    if (!string.IsNullOrEmpty(alias))
                    {
                        if (delete.Condition.Symbol.Contains(alias))
                        {
                            delete.Condition.Symbol = AnalyserHelper.ReplaceSymbol(delete.Condition.Symbol, alias, tableName);
                        }
                    }
                }

                if (delete.Condition != null)
                {
                    this.AppendLine($"WHERE {delete.Condition}");
                }

                this.AppendLine(";");
            }
            else if (statement is DeclareVariableStatement declareVar)
            {
                if (!(this.Option != null && this.Option.NotBuildDeclareStatement))
                {
                    string defaultValue = (declareVar.DefaultValue == null ? "" : $" DEFAULT {declareVar.DefaultValue}");
                    this.AppendLine($"DECLARE {declareVar.Name} {declareVar.DataType}{defaultValue};");
                }

                if (this.Option != null && this.Option.CollectDeclareStatement)
                {
                    this.DeclareStatements.Add(declareVar);
                }
            }
            else if (statement is DeclareTableStatement declareTable)
            {
                this.AppendLine(this.BuildTable(declareTable.TableInfo));
            }
            else if (statement is CreateTableStatement createTable)
            {
                this.AppendLine(this.BuildTable(createTable.TableInfo));
            }
            else if (statement is IfStatement @if)
            {
                foreach (IfStatementItem item in @if.Items)
                {
                    if (item.Type == IfStatementType.IF || item.Type == IfStatementType.ELSEIF)
                    {
                        this.Append($"{item.Type} ");

                        this.BuildIfCondition(item);

                        this.AppendLine(" THEN");
                    }
                    else
                    {
                        this.AppendLine($"{item.Type}");
                    }

                    this.AppendLine("BEGIN");

                    this.AppendChildStatements(item.Statements, true);

                    this.AppendLine("END;");
                }

                this.AppendLine("END IF;");
            }
            else if (statement is CaseStatement @case)
            {
                this.AppendLine($"CASE {@case.VariableName}");

                foreach (IfStatementItem item in @case.Items)
                {
                    if (item.Type != IfStatementType.ELSE)
                    {
                        this.AppendLine($"WHEN {item.Condition} THEN");
                    }
                    else
                    {
                        this.AppendLine("ELSE");
                    }

                    this.AppendLine("BEGIN");
                    this.AppendChildStatements(item.Statements, true);
                    this.AppendLine("END;");
                }

                this.AppendLine("END CASE;");
            }
            else if (statement is SetStatement set)
            {
                if (set.Key != null)
                {
                    if (set.Value != null)
                    {
                        this.AppendLine($"SET {set.Key} = {set.Value};");
                    }
                    else if (set.IsSetCursorVariable && set.ValueStatement != null)
                    {
                        var declareCursorStatement = this.DeclareStatements.FirstOrDefault(item => (item is DeclareCursorStatement) && (item as DeclareCursorStatement).CursorName.Symbol == set.Key.Symbol) as DeclareCursorStatement;

                        if (declareCursorStatement == null)
                        {
                            this.AppendLine($"SET {set.Key} =");

                            this.BuildSelectStatement(set.ValueStatement);
                        }
                        else
                        {
                            declareCursorStatement.SelectStatement = set.ValueStatement;
                        }
                    }
                }
            }
            else if (statement is LoopStatement loop)
            {
                TokenInfo name = loop.Name;

                if (loop.Type != LoopType.LOOP)
                {
                    bool hasExitStatement = AnalyserHelper.HasExitStatement(loop);
                    string label = hasExitStatement ?  this.GetNextLoopLabel("w") : "";

                    if (loop.Condition == null)
                    {
                        this.AppendLine($"{label}WHILE 1=1 DO");
                    }
                    else
                    {
                        this.AppendLine($"{label}WHILE {loop.Condition} DO");
                    }
                }
                else
                {
                    this.AppendLine("LOOP");
                }

                this.AppendLine("BEGIN");

                this.AppendChildStatements(loop.Statements, true);

                this.AppendLine("END;");

                if (loop.Type != LoopType.LOOP)
                {
                    this.AppendLine($"END WHILE;");
                }
                else
                {
                    this.AppendLine($"END LOOP {(name == null ? "" : name + ":")};");
                }
            }
            else if (statement is WhileStatement @while)
            {
                bool hasExitStatement = AnalyserHelper.HasExitStatement(@while);
                string label = hasExitStatement ? this.GetNextLoopLabel("w") : "";

                this.AppendLine($"{label}WHILE {@while.Condition} DO");

                this.AppendChildStatements(@while.Statements, true);

                this.AppendLine("END WHILE;");
            }
            else if (statement is LoopExitStatement whileExit)
            {
                if (!whileExit.IsCursorLoopExit)
                {
                    this.AppendLine($"IF {whileExit.Condition} THEN");
                    this.AppendLine("BEGIN");
                    this.AppendLine($"LEAVE {this.GetCurrentLoopLabel("w")};");
                    this.AppendLine("END;");
                    this.AppendLine("END IF;");
                }
            }
            else if (statement is BreakStatement @break)
            {
                this.AppendLine($"LEAVE {this.GetCurrentLoopLabel("w")};");
            }
            else if (statement is ReturnStatement @return)
            {
                string value = @return.Value?.Symbol;

                if (this.RoutineType != RoutineType.PROCEDURE)
                {
                    this.AppendLine($"RETURN {value};");
                }
                else
                {
                    bool isStringValue = ValueHelper.IsStringValue(value);

                    this.PrintMessage(isStringValue ? StringHelper.HandleSingleQuotationChar(value) : value);

                    //Use it will cause syntax error.
                    //this.AppendLine("RETURN;");
                }
            }
            else if (statement is PrintStatement print)
            {
                this.PrintMessage(print.Content?.Symbol);
            }
            else if (statement is CallStatement call)
            {
                if (!call.IsExecuteSql)
                {
                    string content = string.Join(",", call.Parameters.Select(item => item.Value?.Symbol?.Split('=')?.LastOrDefault()));

                    this.AppendLine($"CALL {call.Name}({content});");
                }
                else
                {
                    string content = call.Parameters.FirstOrDefault()?.Value?.Symbol;

                    if (!string.IsNullOrEmpty(content))
                    {
                        var parameters = call.Parameters.Skip(1);

                        List<CallParameter> usings = new List<CallParameter>();

                        foreach (var parameter in parameters)
                        {
                            var value = parameter.Value?.Symbol;

                            if (!parameter.IsDescription)
                            {
                                usings.Add(parameter);
                            }
                        }

                        string strUsings = usings.Count == 0 ? "" : $" USING {(string.Join(",", usings.Select(item => $"@{item.Value}")))}";

                        this.AppendLine($"SET @SQL:={content};");
                        this.AppendLine("PREPARE dynamicSQL FROM @SQL;");
                        this.AppendLine($"EXECUTE dynamicSQL{strUsings};");
                        this.AppendLine("DEALLOCATE PREPARE dynamicSQL;");
                        this.AppendLine();
                    }
                }
            }
            else if (statement is TransactionStatement transaction)
            {
                TransactionCommandType commandType = transaction.CommandType;

                switch (commandType)
                {
                    case TransactionCommandType.BEGIN:
                        this.AppendLine("START TRANSACTION;");
                        break;
                    case TransactionCommandType.COMMIT:
                        this.AppendLine("COMMIT;");
                        break;
                    case TransactionCommandType.ROLLBACK:
                        this.AppendLine("ROLLBACK;");
                        break;
                }
            }
            else if (statement is LeaveStatement leave)
            {
                this.AppendLine("LEAVE sp;");

                if (this.Option.CollectSpecialStatementTypes.Contains(leave.GetType()))
                {
                    this.SpecialStatements.Add(leave);
                }
            }
            else if (statement is TryCatchStatement tryCatch)
            {
                if (!(this.Option != null && this.Option.NotBuildDeclareStatement))
                {
                    this.AppendLine("DECLARE EXIT HANDLER FOR 1 #[REPLACE ERROR CODE HERE]");
                    this.AppendLine("BEGIN");

                    this.AppendChildStatements(tryCatch.CatchStatements, true);

                    this.AppendLine("END;");
                }

                this.AppendChildStatements(tryCatch.TryStatements, true);

                if (this.Option != null && this.Option.CollectDeclareStatement)
                {
                    this.DeclareStatements.Add(tryCatch);
                }
            }
            else if (statement is ExceptionStatement exception)
            {
                if (!(this.Option != null && this.Option.NotBuildDeclareStatement))
                {
                    foreach (ExceptionItem exceptionItem in exception.Items)
                    {
                        this.AppendLine($"DECLARE EXIT HANDLER FOR {exceptionItem.Name}");
                        this.AppendLine("BEGIN");

                        this.AppendChildStatements(exceptionItem.Statements, true);

                        this.AppendLine("END;");
                    }
                }

                if (this.Option != null && this.Option.CollectDeclareStatement)
                {
                    this.DeclareStatements.Add(exception);
                }
            }
            else if (statement is DeclareCursorStatement declareCursor)
            {
                if (!(this.Option != null && this.Option.NotBuildDeclareStatement))
                {
                    if (declareCursor.SelectStatement != null)
                    {
                        this.AppendLine($"DECLARE {declareCursor.CursorName} CURSOR FOR");

                        this.BuildSelectStatement(declareCursor.SelectStatement);
                    }
                }

                if (this.Option != null && this.Option.CollectDeclareStatement)
                {
                    if (!this.DeclareStatements.Any(item => (item is DeclareCursorStatement) && (item as DeclareCursorStatement).CursorName.Symbol == declareCursor.CursorName.Symbol))
                    {
                        this.DeclareStatements.Add(declareCursor);
                    }
                }
            }
            else if (statement is DeclareCursorHandlerStatement declareCursorHandler)
            {
                if (!(this.Option != null && this.Option.NotBuildDeclareStatement))
                {
                    this.AppendLine($"DECLARE CONTINUE HANDLER");
                    this.AppendLine($"FOR NOT FOUND");
                    this.AppendLine($"BEGIN");
                    this.AppendChildStatements(declareCursorHandler.Statements, true);
                    this.AppendLine($"END;");
                }

                if (this.Option != null && this.Option.CollectDeclareStatement)
                {
                    this.DeclareStatements.Add(declareCursorHandler);
                }
            }
            else if (statement is OpenCursorStatement openCursor)
            {
                this.AppendLine($"OPEN {openCursor.CursorName};");
            }
            else if (statement is FetchCursorStatement fetchCursor)
            {
                if (fetchCursor.Variables.Count > 0)
                {
                    this.AppendLine($"FETCH {fetchCursor.CursorName} INTO {string.Join(",", fetchCursor.Variables)};");
                }
            }
            else if (statement is CloseCursorStatement closeCursor)
            {
                this.AppendLine($"CLOSE {closeCursor.CursorName};");
            }
            else if (statement is TruncateStatement truncate)
            {
                this.AppendLine($"TRUNCATE TABLE {truncate.TableName};");
            }
            else if (statement is DropStatement drop)
            {
                string objectType = drop.ObjectType.ToString().ToUpper();

                this.AppendLine($"DROP {(drop.IsTemporaryTable ? "TEMPORARY" : "")} {objectType} IF EXISTS {drop.ObjectName.NameWithSchema};");
            }
            else if (statement is RaiseErrorStatement error)
            {
                //https://dev.mysql.com/doc/refman/8.0/en/signal.html
                string code = error.ErrorCode == null ? "45000" : error.ErrorCode.Symbol;

                this.AppendLine($"SIGNAL SQLSTATE '{code}' SET MESSAGE_TEXT={error.Content};");
            }
            else if (statement is PreparedStatement prepared)
            {
                PreparedStatementType type = prepared.Type;

                if (type == PreparedStatementType.Prepare)
                {
                    this.AppendLine($"PREPARE {prepared.Id} FROM {prepared.FromSqlOrVariable};");
                }
                else if (type == PreparedStatementType.Execute)
                {
                    string usingVariables = prepared.ExecuteVariables.Count > 0 ? $" USING {(string.Join(",", prepared.ExecuteVariables))}" : "";

                    this.AppendLine($"EXECUTE {prepared.Id}{usingVariables};");
                }
                else if (type == PreparedStatementType.Deallocate)
                {
                    this.AppendLine($"DEALLOCATE PREPARE {prepared.Id};");
                }
            }
            else if (statement is GotoStatement gts)
            {
                if (gts.IsLabel)
                {
                    this.AppendLine($"#GOTO {gts.Label};");
                }
                else
                {
                    this.AppendLine($"#GOTO#{gts.Label}");

                    this.AppendChildStatements(gts.Statements);
                }
            }

            return this;
        }

        protected override void BuildSelectStatement(SelectStatement select, bool appendSeparator = true)
        {
            bool isCreateTemporaryTable = false;

            if (select.IntoTableName != null)
            {
                isCreateTemporaryTable = true;

                this.AppendLine($"CREATE TEMPORARY TABLE IF NOT EXISTS {select.IntoTableName} AS (");
            }

            bool isWith = select.WithStatements != null && select.WithStatements.Count > 0;

            string selectColumns = $"SELECT {string.Join(",", select.Columns.Select(item => this.GetNameWithAlias(item)))}";
            bool hasAssignVariableColumn = this.HasAssignVariableColumn(select);

            if (select.NoTableName && select.Columns.Count == 1 && hasAssignVariableColumn)
            {
                ColumnName columnName = select.Columns.First();

                this.AppendLine($"SET {columnName}");
            }
            else if (!select.NoTableName && hasAssignVariableColumn && (this.RoutineType == RoutineType.FUNCTION || this.RoutineType == RoutineType.TRIGGER))
            {
                //use "select column1, column2 into var1, var2" instead of "select var1=column1, var2=column2"

                List<string> variables = new List<string>();
                List<string> columnNames = new List<string>();

                foreach (var column in select.Columns)
                {
                    if (column.Symbol.Contains("="))
                    {
                        string[] items = column.Symbol.Split('=');
                        string variable = items[0].Trim();
                        string columName = items[1].Trim();

                        variables.Add(variable);
                        columnNames.Add(columName);
                    }
                }

                this.AppendLine($"SELECT {string.Join(",", columnNames)} INTO {string.Join(",", variables)}");
            }
            else if (!isWith)
            {
                this.AppendLine(selectColumns);
            }

            Action appendWith = () =>
            {
                int i = 0;

                foreach (WithStatement withStatement in select.WithStatements)
                {
                    if (i == 0)
                    {
                        this.AppendLine($"WITH {withStatement.Name}");
                    }
                    else
                    {
                        this.AppendLine($",{withStatement.Name}");
                    }

                    this.AppendLine("AS(");

                    this.AppendChildStatements(withStatement.SelectStatements, false);

                    this.AppendLine(")");

                    i++;
                }

            };

            Action appendFrom = () =>
            {
                if (select.HasFromItems)
                {
                    this.BuildSelectStatementFromItems(select);
                }
                else if (select.TableName != null)
                {
                    this.AppendLine($"FROM {this.GetNameWithAlias(select.TableName)}");
                }
            };

            if (isWith)
            {
                appendWith();

                this.AppendLine(selectColumns);
            }

            appendFrom();

            if (select.Where != null)
            {
                this.AppendLine($"WHERE {select.Where}");
            }

            if (select.GroupBy != null && select.GroupBy.Count > 0)
            {
                this.AppendLine($"GROUP BY {string.Join(",", select.GroupBy.Select(item => item))}");
            }

            if (select.Having != null)
            {
                this.AppendLine($"HAVING {select.Having}");
            }

            if (select.OrderBy != null && select.OrderBy.Count > 0)
            {
                this.AppendLine($"ORDER BY {string.Join(",", select.OrderBy.Select(item => item))}");
            }

            if (select.TopInfo != null)
            {
                this.AppendLine($"LIMIT 0,{select.TopInfo.TopCount}");
            }

            if (select.LimitInfo != null)
            {
                this.AppendLine($"LIMIT {select.LimitInfo.StartRowIndex},{select.LimitInfo.RowCount}");
            }

            if (select.UnionStatements != null)
            {
                foreach (var union in select.UnionStatements)
                {
                    this.Build(union, false).TrimSeparator();
                    this.AppendLine();
                }
            }

            if (isCreateTemporaryTable)
            {
                this.AppendLine(")");
            }

            if (appendSeparator)
            {
                this.AppendLine(";", false);
            }
        }

        private string GetUnionTypeName(UnionType unionType)
        {
            switch (unionType)
            {
                case UnionType.UNION_ALL:
                    return "UNION ALL";
                default:
                    return unionType.ToString();
            }
        }

        private void PrintMessage(string content)
        {
            this.AppendLine($"SELECT {content};");
        }

        public string BuildTable(TableInfo table)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"CREATE {(table.IsTemporary ? "TEMPORARY" : "")} TABLE {table.Name}(");

            int i = 0;

            foreach (var column in table.Columns)
            {
                string identity = column.IsIdentity ? " AUTO_INCREMENT PRIMARY KEY" : "";

                sb.AppendLine($"{column.Name.FieldName} {column.DataType}{identity}{(i == table.Columns.Count - 1 ? "" : ",")}");

                i++;
            }

            sb.AppendLine(");");

            return sb.ToString();
        }
    }
}
