-- Applied only to the operator-selected database or the app's working copy.
-- The bundled MDF/LDF is never attached by the modern application.
-- Existing academic tables/rows and identities are retained. No DROP/TRUNCATE.
-- All DDL runs in the caller's transaction. Legacy constraints remain untouched.
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.DepartmentTbl', N'U') IS NULL
    CREATE TABLE dbo.DepartmentTbl (
        DeptId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(100) NOT NULL,
        Intake int NOT NULL CHECK (Intake BETWEEN 1 AND 100000),
        Fees decimal(18,2) NOT NULL CHECK (Fees >= 0));

IF OBJECT_ID(N'dbo.StudentTbl', N'U') IS NULL
    CREATE TABLE dbo.StudentTbl (
        StId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(100) NOT NULL, DOB date NOT NULL,
        Gender nvarchar(100) NOT NULL, Address nvarchar(200) NOT NULL,
        DeptId int NOT NULL REFERENCES dbo.DepartmentTbl(DeptId),
        Department nvarchar(100) NOT NULL,
        Phone nvarchar(30) NOT NULL, Semester nvarchar(100) NOT NULL);

IF OBJECT_ID(N'dbo.FacultyTbl', N'U') IS NULL
    CREATE TABLE dbo.FacultyTbl (
        F_Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(100) NOT NULL, DOB date NOT NULL,
        Gender nvarchar(100) NOT NULL, Address nvarchar(200) NOT NULL,
        Qualification nvarchar(60) NOT NULL,
        Experience int NOT NULL CHECK (Experience BETWEEN 0 AND 80),
        DeptId int NOT NULL REFERENCES dbo.DepartmentTbl(DeptId),
        Department nvarchar(100) NOT NULL,
        Salary decimal(18,2) NOT NULL CHECK (Salary >= 0));

IF OBJECT_ID(N'dbo.CourseTbl', N'U') IS NULL
    CREATE TABLE dbo.CourseTbl (
        Course_Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Course_Name nvarchar(100) NOT NULL,
        Credit_Hrs int NOT NULL CHECK (Credit_Hrs BETWEEN 1 AND 30),
        DeptId int NOT NULL REFERENCES dbo.DepartmentTbl(DeptId),
        Department nvarchar(100) NOT NULL,
        Fac_Id int NOT NULL REFERENCES dbo.FacultyTbl(F_Id),
        Fac_Name nvarchar(100) NOT NULL);

IF OBJECT_ID(N'dbo.CampusTbl', N'U') IS NULL
    CREATE TABLE dbo.CampusTbl (
        Camp_Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(100) NOT NULL, City nvarchar(80) NOT NULL,
        Director nvarchar(100) NOT NULL,
        Rank nvarchar(50) NOT NULL,
        Join_Date date NOT NULL);

IF OBJECT_ID(N'dbo.FeesTbl', N'U') IS NULL
    CREATE TABLE dbo.FeesTbl (
        Fee_Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        StId int NOT NULL REFERENCES dbo.StudentTbl(StId),
        StName nvarchar(100) NOT NULL, StDept nvarchar(100) NOT NULL,
        Amount decimal(18,2) NOT NULL CHECK (Amount > 0),
        Semester nvarchar(100) NOT NULL, PayDate date NOT NULL);

IF OBJECT_ID(N'dbo.SalaryTbl', N'U') IS NULL
    CREATE TABLE dbo.SalaryTbl (
        Salary_Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        F_Id int NOT NULL REFERENCES dbo.FacultyTbl(F_Id),
        F_Name nvarchar(100) NOT NULL, Department nvarchar(100) NOT NULL,
        Salary decimal(18,2) NOT NULL CHECK (Salary > 0), PayDate date NOT NULL);

IF OBJECT_ID(N'dbo.UmsAccounts', N'U') IS NULL
    CREATE TABLE dbo.UmsAccounts (
        AccountId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Username nvarchar(100) COLLATE Latin1_General_100_CI_AS NOT NULL UNIQUE,
        PasswordHash nvarchar(200) NOT NULL,
        Role nvarchar(30) NOT NULL CHECK (Role IN (N'Administrator', N'Registrar')),
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());

IF OBJECT_ID(N'dbo.UmsSchemaMetadata', N'U') IS NULL
    CREATE TABLE dbo.UmsSchemaMetadata (
        [Key] nvarchar(100) NOT NULL PRIMARY KEY, [Value] nvarchar(500) NOT NULL);

IF OBJECT_ID(N'dbo.UmsLegacyAccountMap', N'U') IS NULL
    CREATE TABLE dbo.UmsLegacyAccountMap (
        MigrationOrdinal int NOT NULL PRIMARY KEY,
        OriginalUsername nvarchar(4000) NOT NULL,
        NewUsername nvarchar(100) NOT NULL UNIQUE);

-- Conversion of existing monetary columns is validated and performed by
-- SqlUniversityStore in this same transaction. It refuses rounding/data loss.
-- Account import is delayed until a first modern Administrator is established.
