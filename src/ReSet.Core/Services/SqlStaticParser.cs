using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ReSet.Core.Models;
using Serilog;

namespace ReSet.Core.Services
{
    public class SqlStaticParser
    {
        public SpStaticAnalysisResult Analyze(string ddlText)
        {
            var result = new SpStaticAnalysisResult();
            if (string.IsNullOrWhiteSpace(ddlText))
            {
                result.IsParsedSuccessfully = false;
                result.ParserWarningMessage = "DDL 텍스트가 비어 있습니다.";
                return result;
            }

            try
            {
                var parser = new TSql160Parser(true);
                using (var reader = new StringReader(ddlText))
                {
                    var fragment = parser.Parse(reader, out var errors);
                    if (errors != null && errors.Count > 0)
                    {
                        result.IsParsedSuccessfully = false;
                        var sb = new StringBuilder();
                        sb.AppendLine("T-SQL 구문 오류 감지 (Soft Fail 적용):");
                        foreach (var err in errors)
                        {
                            sb.AppendLine($"- Line {err.Line}, Col {err.Column}: {err.Message}");
                        }
                        result.ParserWarningMessage = sb.ToString();
                        Log.Warning("[SqlStaticParser] T-SQL 정적 파싱 구문 오류 발생 - {Errors}", result.ParserWarningMessage);
                        return result;
                    }

                    if (fragment != null)
                    {
                        var visitor = new SpStructureVisitor();
                        fragment.Accept(visitor);

                        result.IsParsedSuccessfully = true;
                        result.ReferencedTables = visitor.ReferencedTables;
                        result.CreatedTempTables = visitor.CreatedTempTables;
                        result.ControlFlowSummary = visitor.ControlFlowSummary;
                        result.SelectTables = visitor.SelectTables;
                        result.InsertTables = visitor.InsertTables;
                        result.UpdateTables = visitor.UpdateTables;
                        result.DeleteTables = visitor.DeleteTables;
                    }
                }
            }
            catch (Exception ex)
            {
                result.IsParsedSuccessfully = false;
                result.ParserWarningMessage = $"정적 파싱 예외 발생: {ex.Message}";
                Log.Error(ex, "[SqlStaticParser] 예외 발생 (Soft Fail)");
            }

            return result;
        }
    }

    internal class SpStructureVisitor : TSqlFragmentVisitor
    {
        public List<string> ReferencedTables { get; } = new();
        public List<string> CreatedTempTables { get; } = new();
        public List<string> ControlFlowSummary { get; } = new();

        public List<string> SelectTables { get; } = new();
        public List<string> InsertTables { get; } = new();
        public List<string> UpdateTables { get; } = new();
        public List<string> DeleteTables { get; } = new();

        private readonly HashSet<string> _foundTables = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _foundTemps = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _foundSelect = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _foundInsert = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _foundUpdate = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _foundDelete = new(StringComparer.OrdinalIgnoreCase);

        private readonly Stack<string> _statementContext = new();
        private int _indentLevel = 0;

        // CRUD Statement 방문 감지 및 컨텍스트 스택 처리 (ExplicitVisit 적용)
        public override void ExplicitVisit(SelectStatement node)
        {
            _statementContext.Push("SELECT");
            base.ExplicitVisit(node);
            _statementContext.Pop();
        }

        public override void ExplicitVisit(InsertStatement node)
        {
            _statementContext.Push("INSERT");
            base.ExplicitVisit(node);
            _statementContext.Pop();
        }

        public override void ExplicitVisit(UpdateStatement node)
        {
            _statementContext.Push("UPDATE");
            base.ExplicitVisit(node);
            _statementContext.Pop();
        }

        public override void ExplicitVisit(DeleteStatement node)
        {
            _statementContext.Push("DELETE");
            base.ExplicitVisit(node);
            _statementContext.Pop();
        }

        // Specification 단위 상세 감지 (ExplicitVisit 적용)
        public override void ExplicitVisit(InsertSpecification node)
        {
            _statementContext.Push("INSERT");
            base.ExplicitVisit(node);
            _statementContext.Pop();
        }

        public override void ExplicitVisit(UpdateSpecification node)
        {
            _statementContext.Push("UPDATE");
            base.ExplicitVisit(node);
            _statementContext.Pop();
        }

        public override void ExplicitVisit(DeleteSpecification node)
        {
            _statementContext.Push("DELETE");
            base.ExplicitVisit(node);
            _statementContext.Pop();
        }

        public override void ExplicitVisit(QuerySpecification node)
        {
            _statementContext.Push("SELECT");
            base.ExplicitVisit(node);
            _statementContext.Pop();
        }

        // 1. 참조하는 테이블명 방문 수집 (NamedTableReference - ExplicitVisit 적용)
        public override void ExplicitVisit(NamedTableReference node)
        {
            base.ExplicitVisit(node);
            if (node.SchemaObject != null)
            {
                var tableName = GetSchemaObjectString(node.SchemaObject);
                if (!string.IsNullOrWhiteSpace(tableName))
                {
                    if (tableName.StartsWith("#"))
                    {
                        if (_foundTemps.Add(tableName))
                        {
                            CreatedTempTables.Add(tableName);
                        }
                    }
                    else
                    {
                        if (_foundTables.Add(tableName))
                        {
                            ReferencedTables.Add(tableName);
                        }

                        // CRUD 분류 수집
                        var currentContext = _statementContext.Count > 0 ? _statementContext.Peek() : "SELECT";
                        switch (currentContext)
                        {
                            case "SELECT":
                                if (_foundSelect.Add(tableName)) SelectTables.Add(tableName);
                                break;
                            case "INSERT":
                                if (_foundInsert.Add(tableName)) InsertTables.Add(tableName);
                                break;
                            case "UPDATE":
                                if (_foundUpdate.Add(tableName)) UpdateTables.Add(tableName);
                                break;
                            case "DELETE":
                                if (_foundDelete.Add(tableName)) DeleteTables.Add(tableName);
                                break;
                        }
                    }
                }
            }
        }

        // 2. IF 조건 분기 구조 방문 수집 (ExplicitVisit 및 들여쓰기 적용)
        public override void ExplicitVisit(IfStatement node)
        {
            var indent = new string(' ', _indentLevel * 2);
            var line = node.StartLine;
            var condText = GetNodeSqlText(node.Predicate);
            ControlFlowSummary.Add($"{indent}Line {line}: IF ({condText})");

            _indentLevel++;
            base.ExplicitVisit(node);
            _indentLevel--;
        }

        // 3. WHILE 루프 분기 구조 방문 수집 (ExplicitVisit 및 들여쓰기 적용)
        public override void ExplicitVisit(WhileStatement node)
        {
            var indent = new string(' ', _indentLevel * 2);
            var line = node.StartLine;
            var condText = GetNodeSqlText(node.Predicate);
            ControlFlowSummary.Add($"{indent}Line {line}: WHILE ({condText})");

            _indentLevel++;
            base.ExplicitVisit(node);
            _indentLevel--;
        }

        private string GetSchemaObjectString(SchemaObjectName schemaObject)
        {
            var parts = new List<string>();
            if (schemaObject.ServerIdentifier != null) parts.Add(schemaObject.ServerIdentifier.Value);
            if (schemaObject.DatabaseIdentifier != null) parts.Add(schemaObject.DatabaseIdentifier.Value);
            if (schemaObject.SchemaIdentifier != null) parts.Add(schemaObject.SchemaIdentifier.Value);
            if (schemaObject.BaseIdentifier != null) parts.Add(schemaObject.BaseIdentifier.Value);

            return string.Join(".", parts);
        }

        private string GetNodeSqlText(TSqlFragment node)
        {
            if (node == null) return "Unknown Condition";
            var sb = new StringBuilder();
            for (int i = node.FirstTokenIndex; i <= node.LastTokenIndex; i++)
            {
                if (i >= 0 && node.ScriptTokenStream != null && i < node.ScriptTokenStream.Count)
                {
                    sb.Append(node.ScriptTokenStream[i].Text);
                }
            }
            var cond = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(cond) ? "Predicate Details" : cond;
        }
    }
}
