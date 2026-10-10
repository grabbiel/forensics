-- Measures the inbox query (GET /api/v1/evidence) as EF Core sends it: the first page, a custodian-filtered page, a
-- text search, and the last page, fetched once by keyset with its cursor supplied (what the API does) and once by OFFSET
-- (the alternative it does not use); then a search that matches nothing, and what a custody write's update of its inbox
-- row costs (rolled back). Each step prints its logical reads, elapsed time and executed plan.
-- Needs go-sqlcmd (brew install sqlcmd):
--
--   sqlcmd -S localhost,1433 -U sa -P 'DevOnly_Passw0rd!2026' -C -d EvidenceChain -W -i database/measure-inbox.sql \
--     | grep -E '^[0-9]\.|logical reads|elapsed time|\|--'
SET NOCOUNT ON;

DECLARE @page int = 26; -- 25 rows, plus one that says whether another page follows
DECLARE @rows int = (SELECT COUNT(*) FROM dbo.EvidenceInbox);
DECLARE @depth int = @rows - 25; -- rows before the last page
DECLARE @custodianId int = (SELECT TOP (1) CurrentCustodianId FROM dbo.EvidenceInbox GROUP BY CurrentCustodianId ORDER BY COUNT(*) DESC);
DECLARE @at datetime2(7), @id bigint; -- the last row of the page before the last, as a cursor carries it
DECLARE @written bigint = (SELECT TOP (1) EvidenceId FROM dbo.EvidenceInbox ORDER BY LastEventAtUtc, EvidenceId); -- step 7's row
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
SET STATISTICS IO, TIME, PROFILE ON;

PRINT '1. First page';
SET @sql = @select + @newestFirst;
EXEC sp_executesql @sql, N'@p int', @p = @page;

PRINT '2. First page for one custodian';
SET @sql = @select + N' WHERE [e].[CurrentCustodianId] = @custodianId' + @newestFirst;
EXEC sp_executesql @sql, N'@p int, @custodianId int', @p = @page, @custodianId = @custodianId;

PRINT '3. Text search for "firewall" (also tried as a code prefix, as the API does)';
SET @sql = @select + N' WHERE [e].[Code] LIKE @prefix ESCAPE ''\'' OR [e].[Description] LIKE @contains ESCAPE N''\''' + @newestFirst;
EXEC sp_executesql @sql, N'@p int, @prefix varchar(15), @contains nvarchar(500)', @p = @page, @prefix = 'FIREWALL%', @contains = N'%firewall%';

PRINT '4. Last page by keyset (one page; the cursor, the previous page''s last row, is supplied)';
SET @sql = @select + N' WHERE [e].[LastEventAtUtc] < @at OR ([e].[LastEventAtUtc] = @at AND [e].[EvidenceId] < @id)' + @newestFirst;
EXEC sp_executesql @sql, N'@p int, @at datetime2(7), @id bigint', @p = @page, @at = @at, @id = @id;

PRINT '5. Last page by OFFSET (not used: it reads every skipped row)';
SET @sql = REPLACE(@select, N'TOP(@p) ', N'') + @newestFirst + N' OFFSET @skip ROWS FETCH NEXT @p ROWS ONLY';
EXEC sp_executesql @sql, N'@p int, @skip int', @p = @page, @skip = @depth;

PRINT '6. Text search that matches nothing (no early stop: every row is read)';
SET @sql = @select + N' WHERE [e].[Code] LIKE @prefix ESCAPE ''\'' OR [e].[Description] LIKE @contains ESCAPE N''\''' + @newestFirst;
EXEC sp_executesql @sql, N'@p int, @prefix varchar(15), @contains nvarchar(500)', @p = @page, @prefix = 'ZQXJ%', @contains = N'%zqxj%';

PRINT '7. A custody write''s update of its inbox row (rolled back): LastEventAtUtc is in every keyset index''s key';
BEGIN TRANSACTION;
UPDATE dbo.EvidenceInbox SET LastEventAtUtc = SYSUTCDATETIME(), EventCount = EventCount + 1 WHERE EvidenceId = @written;
ROLLBACK;

SET STATISTICS IO, TIME, PROFILE OFF;
