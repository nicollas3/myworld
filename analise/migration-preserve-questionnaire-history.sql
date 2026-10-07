BEGIN TRANSACTION;
ALTER TABLE [QuestionnaireResponses] ADD [DefinitionSnapshotJson] nvarchar(max) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261007023438_PreserveQuestionnaireHistory', N'10.0.12');

COMMIT;
GO

