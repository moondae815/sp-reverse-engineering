using System;
using Xunit;
using ReSet.Core.Services;

namespace ReSet.Core.Tests
{
    public class SqlStaticParserTests
    {
        [Fact]
        public void Analyze_WithValidStoredProcedure_ShouldExtractTablesAndControlFlow()
        {
            // Arrange
            var ddlText = @"
CREATE PROCEDURE dbo.CalculateBonus
    @EmployeeID INT,
    @Year INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 임시 테이블 생성 및 사용
    CREATE TABLE #TempBonus (
        EmpID INT,
        BonusAmount DECIMAL(18,2)
    );

    IF @Year >= 2026
    BEGIN
        INSERT INTO #TempBonus (EmpID, BonusAmount)
        SELECT e.EmployeeID, 1000.00
        FROM dbo.Employees e
        JOIN dbo.Departments d ON e.DeptID = d.ID
        WHERE e.EmployeeID = @EmployeeID;
    END
    ELSE
    BEGIN
        INSERT INTO #TempBonus (EmpID, BonusAmount)
        SELECT EmployeeID, 500.00
        FROM dbo.Employees
        WHERE EmployeeID = @EmployeeID;
    END

    -- WHILE 루프 예시
    DECLARE @Counter INT = 0;
    WHILE @Counter < 5
    BEGIN
        UPDATE dbo.AuditLog
        SET LogDate = GETDATE()
        WHERE ID = @Counter;

        SET @Counter = @Counter + 1;
    END

    SELECT * FROM #TempBonus;
    DROP TABLE #TempBonus;
END;
";
            var parser = new SqlStaticParser();

            // Act
            var result = parser.Analyze(ddlText);

            // Assert
            Assert.True(result.IsParsedSuccessfully);
            Assert.Null(result.ParserWarningMessage);

            // 참조 테이블 검증
            Assert.Contains("dbo.Employees", result.ReferencedTables);
            Assert.Contains("dbo.Departments", result.ReferencedTables);
            Assert.Contains("dbo.AuditLog", result.ReferencedTables);

            // 임시 테이블 검증
            Assert.Contains("#TempBonus", result.CreatedTempTables);

            // 제어 흐름 검증
            Assert.NotEmpty(result.ControlFlowSummary);
            Assert.Contains(result.ControlFlowSummary, s => s.Contains("IF") && s.Contains("@Year >= 2026"));
            Assert.Contains(result.ControlFlowSummary, s => s.Contains("WHILE") && s.Contains("@Counter < 5"));
        }

        [Fact]
        public void Analyze_WithInvalidSqlSyntax_ShouldSoftFailAndReturnErrors()
        {
            // Arrange
            var invalidDdl = @"
CREATE PROCEDURE dbo.BadProc
AS
BEGIN
    -- 의도적인 문법 에러 (SELECT 절에 FROM 생략 및 콤마 오류)
    SELECT Col1 Col2 dbo.MyTable;
END;
";
            var parser = new SqlStaticParser();

            // Act
            var result = parser.Analyze(invalidDdl);

            // Assert
            Assert.False(result.IsParsedSuccessfully);
            Assert.NotNull(result.ParserWarningMessage);
            Assert.Contains("T-SQL 구문 오류 감지", result.ParserWarningMessage);
            Assert.Empty(result.ReferencedTables);
        }

        [Fact]
        public void Analyze_WithEmptyDdl_ShouldSoftFailGracefully()
        {
            // Arrange
            var parser = new SqlStaticParser();

            // Act
            var result = parser.Analyze(string.Empty);

            // Assert
            Assert.False(result.IsParsedSuccessfully);
            Assert.Equal("DDL 텍스트가 비어 있습니다.", result.ParserWarningMessage);
        }
    }
}
