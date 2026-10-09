-- Measures the inbox query (GET /api/v1/evidence) as EF Core sends it, under SET STATISTICS IO, TIME ON:
-- the first page, a custodian-filtered page, a text search, and the last page reached by keyset (what the API does)
-- and by OFFSET (the alternative it does not use). Read each step's "logical reads" and "elapsed time" lines.
--
--   sqlcmd -S localhost,1433 -U sa -P 'DevOnly_Passw0rd!2026' -C -d EvidenceChain -i database/measure-inbox.sql \
--     | grep -E '^[0-9]\.|logical reads|elapsed time'
SET NOCOUNT ON;

DECLARE @page int = 26; -- 25 rows, plus one that says whether another page follows
DECLARE @rows int = (SELECT COUNT(*) FROM dbo.EvidenceInbox);
DECLARE @depth int = @rows - 25; -- rows before the last page
DECLARE @custodianId int = (SELECT TOP (1) CurrentCustodianId FROM dbo.EvidenceInbox GROUP BY CurrentCustodianId ORDER BY COUNT(*) DESC);
DECLARE @at datetime2(7), @id bigint; -- the last row of the page before the last, as a cursor carries it
SELECT @at = LastEventAtUtc, @id = EvidenceId
FROM dbo.EvidenceInbox
ORDER BY LastEventAtUtc DESC, EvidenceId DESC
OFFSET @depth - 1 ROWS FETCH NEXT 1 ROWS ONLY;

DECLARE @select nvarchar(max) = N'SELECT TOP(@p) [e].[LastEventAtUtc], [e].[EvidenceId], [e].[Code], [e].[TypeCode], [e].[Description],
    [e].[CurrentCustodianId], [e].[CurrentCustodianName], [e].[EventCount], [e].[IntegrityStatus], [e].[IntegrityCheckedAtUtc],
    [e].[PendingTransferId], [e].[PendingToCustodianId], [e].[PendingSinceUtc]
FROM [EvidenceInbox] AS [e]';
DECLARE @newestFirst nvarchar(100) = N' ORDER BY [e].[LastEventAtUtc] DESC, [e].[EvidenceId] DESC';
DECLARE @sql nvarchar(max);

PRINT CONCAT('EvidenceInbox rows: ', @rows, '; last page starts after row ', @depth);
SET STATISTICS IO, TIME ON;

PRINT '1. First page';
SET @sql = @select + @newestFirst;
EXEC sp_executesql @sql, N'@p int', @p = @page;

PRINT '2. First page for one custodian';
SET @sql = @select + N' WHERE [e].[CurrentCustodianId] = @custodianId' + @newestFirst;
EXEC sp_executesql @sql, N'@p int, @custodianId int', @p = @page, @custodianId = @custodianId;

PRINT '3. Text search (code prefix or description)';
SET @sql = @select + N' WHERE [e].[Code] LIKE @prefix ESCAPE ''\'' OR [e].[Description] LIKE @contains ESCAPE N''\''' + @newestFirst;
EXEC sp_executesql @sql, N'@p int, @prefix varchar(15), @contains nvarchar(500)', @p = @page, @prefix = 'FIREWALL%', @contains = N'%firewall%';

PRINT '4. Last page by keyset (the cursor carries the previous page''s last row)';
SET @sql = @select + N' WHERE [e].[LastEventAtUtc] < @at OR ([e].[LastEventAtUtc] = @at AND [e].[EvidenceId] < @id)' + @newestFirst;
EXEC sp_executesql @sql, N'@p int, @at datetime2(7), @id bigint', @p = @page, @at = @at, @id = @id;

PRINT '5. Last page by OFFSET (not used: it reads every skipped row)';
SET @sql = REPLACE(@select, N'TOP(@p) ', N'') + @newestFirst + N' OFFSET @skip ROWS FETCH NEXT @p ROWS ONLY';
EXEC sp_executesql @sql, N'@p int, @skip int', @p = @page, @skip = @depth;

SET STATISTICS IO, TIME OFF;
