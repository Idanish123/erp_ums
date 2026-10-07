-- KIET Karachi academic context with explicitly fictional demonstration records.
-- Run against the intended initialized UMS database after taking a backup.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM dbo.UmsSchemaMetadata WITH (UPDLOCK,HOLDLOCK)
               WHERE [Key] = N'KIETSampleData.v1')
    BEGIN
        COMMIT;
        PRINT 'KIET sample data v1 already installed; no records changed.';
        RETURN;
    END;

    DECLARE @before TABLE (Entity varchar(20), Records int);
    INSERT @before VALUES
    ('Departments',(SELECT COUNT(*) FROM dbo.DepartmentTbl)),
    ('Campuses',(SELECT COUNT(*) FROM dbo.CampusTbl)),
    ('Faculty',(SELECT COUNT(*) FROM dbo.FacultyTbl)),
    ('Students',(SELECT COUNT(*) FROM dbo.StudentTbl)),
    ('Courses',(SELECT COUNT(*) FROM dbo.CourseTbl)),
    ('Fees',(SELECT COUNT(*) FROM dbo.FeesTbl)),
    ('Salaries',(SELECT COUNT(*) FROM dbo.SalaryTbl));

    DECLARE @computing int, @management int;
    DECLARE @computingName varchar(50) = 'KIET Computing & Info Sciences [Sample]',
            @managementName varchar(50) = 'KIET Management Sciences [Sample]';
    INSERT dbo.DepartmentTbl (Name,Intake,Fees) VALUES (@computingName,240,132000);
    SET @computing = CONVERT(int,SCOPE_IDENTITY());
    INSERT dbo.DepartmentTbl (Name,Intake,Fees) VALUES (@managementName,180,120000);
    SET @management = CONVERT(int,SCOPE_IDENTITY());

    INSERT dbo.CampusTbl (Name,City,Director,Rank,Join_Date) VALUES
    ('KIET Ahsanabad Campus [Sample]','Karachi','Sample Campus Coordinator 01','Sample - no official rank','20260101'),
    ('KIET Shahrah-e-Faisal Campus [Sample]','Karachi','Sample Campus Coordinator 02','Sample - no official rank','20260101'),
    ('KIET North Nazimabad Campus [Sample]','Karachi','Sample Campus Coordinator 03','Sample - no official rank','20260101');

    DECLARE @facultySeed TABLE (Seq int PRIMARY KEY, Name varchar(50), Gender varchar(10));
    INSERT @facultySeed VALUES
    (1,'Dr. Sara Farooq','Female'),(2,'Dr. Imran Siddiqui','Male'),
    (3,'Ms. Nadia Qureshi','Female'),(4,'Mr. Hassan Javed','Male'),
    (5,'Dr. Amina Khan','Female'),(6,'Mr. Umar Rehman','Male'),
    (7,'Dr. Farah Ali','Female'),(8,'Mr. Kamran Ahmed','Male'),
    (9,'Ms. Hina Shah','Female'),(10,'Dr. Salman Malik','Male'),
    (11,'Ms. Rabia Yousuf','Female'),(12,'Mr. Tariq Abbas','Male');
    DECLARE @faculty TABLE (Seq int PRIMARY KEY, Id int, Name varchar(50), DeptId int, Department varchar(50), Salary decimal(18,2));
    DECLARE @i int = 1, @name varchar(50), @gender varchar(10), @dept int,
            @department varchar(50), @salary decimal(18,2);
    WHILE @i <= 12
    BEGIN
        SELECT @name = Name + ' [Sample]', @gender = Gender FROM @facultySeed WHERE Seq = @i;
        SET @dept = CASE WHEN @i <= 6 THEN @computing ELSE @management END;
        SET @department = CASE WHEN @i <= 6 THEN @computingName ELSE @managementName END;
        SET @salary = 150000 + @i * 5000;
        INSERT dbo.FacultyTbl (Name,DOB,Gender,Address,Qualification,Experience,DeptId,Department,Salary)
        VALUES (@name,DATEFROMPARTS(1979 + @i,1 + @i % 12,10),@gender,
                'Sample faculty address - Karachi ' + CONVERT(varchar(2),@i),
                CASE WHEN @i <= 6 THEN 'MS Computer Science' ELSE 'MBA' END,
                4 + @i,@dept,@department,@salary);
        INSERT @faculty VALUES (@i,CONVERT(int,SCOPE_IDENTITY()),@name,@dept,@department,@salary);
        SET @i += 1;
    END;

    DECLARE @studentSeed TABLE (Seq int PRIMARY KEY, Name varchar(50), Gender varchar(10));
    INSERT @studentSeed VALUES
    (1,'Ayesha Ahmed','Female'),(2,'Bilal Khan','Male'),(3,'Fatima Noor','Female'),(4,'Hamza Ali','Male'),
    (5,'Hira Siddiqui','Female'),(6,'Ibrahim Shah','Male'),(7,'Mahnoor Raza','Female'),(8,'Omar Hassan','Male'),
    (9,'Sana Tariq','Female'),(10,'Zain Ahmed','Male'),(11,'Anum Farooq','Female'),(12,'Danish Iqbal','Male'),
    (13,'Eman Malik','Female'),(14,'Fahad Hussain','Male'),(15,'Iqra Yousuf','Female'),(16,'Junaid Akhtar','Male'),
    (17,'Laiba Asif','Female'),(18,'Muneeb Qureshi','Male'),(19,'Nadia Saleem','Female'),(20,'Osama Saeed','Male'),
    (21,'Rabia Khalid','Female'),(22,'Saad Ansari','Male'),(23,'Tuba Nadeem','Female'),(24,'Usman Ahmed','Male'),
    (25,'Warda Imran','Female'),(26,'Yasir Abbas','Male'),(27,'Zoya Khan','Female'),(28,'Ahmed Bilal','Male'),
    (29,'Bushra Hasan','Female'),(30,'Faisal Rafiq','Male'),(31,'Khadija Akram','Female'),(32,'Luqman Siddiq','Male'),
    (33,'Maryam Faisal','Female'),(34,'Noman Ali','Male'),(35,'Rida Fatima','Female'),(36,'Salman Rehman','Male'),
    (37,'Sobia Shahid','Female'),(38,'Talha Nisar','Male'),(39,'Umaima Hasan','Female'),(40,'Waleed Aslam','Male');
    DECLARE @students TABLE (Seq int PRIMARY KEY, Id int, Name varchar(50), Department varchar(50), Semester int, Fee decimal(18,2));
    DECLARE @semester int;
    SET @i = 1;
    WHILE @i <= 40
    BEGIN
        SELECT @name = Name + ' [Sample]', @gender = Gender FROM @studentSeed WHERE Seq = @i;
        SET @dept = CASE WHEN @i <= 20 THEN @computing ELSE @management END;
        SET @department = CASE WHEN @i <= 20 THEN @computingName ELSE @managementName END;
        SET @semester = 1 + (@i - 1) % 8;
        INSERT dbo.StudentTbl (Name,DOB,Gender,Address,DeptId,Department,Phone,Semester)
        VALUES (@name,DATEFROMPARTS(2002 + @i % 6,1 + @i % 12,12),@gender,
                'Sample address - Karachi ' + CONVERT(varchar(2),@i),@dept,@department,
                'DEMO-ST-' + RIGHT('000' + CONVERT(varchar(3),@i),3),@semester);
        INSERT @students VALUES (@i,CONVERT(int,SCOPE_IDENTITY()),@name,@department,@semester,
                                 CASE WHEN @i <= 20 THEN 132000 ELSE 120000 END);
        SET @i += 1;
    END;

    DECLARE @courses TABLE (Seq int, Name varchar(50), Credits int);
    INSERT @courses VALUES
    (1,'Programming Fundamentals',3),(2,'Object Oriented Programming',3),
    (3,'Database Systems',3),(4,'Data Structures',3),(5,'Information Security',2),
    (6,'Artificial Intelligence',2),(7,'Computer Networks',2),(8,'Software Engineering',3),
    (9,'Operating Systems',2),(10,'HCI & Computer Graphics',2),(11,'Theory of Automata',3),
    (12,'Advance Database Management Systems',2),
    (13,'Principles of Management',3),(14,'Business Mathematics',3),(15,'Economics',2),
    (16,'Intermediate Accounting',3),(17,'Principles of Marketing',3),(18,'Business Technology',3),
    (19,'Introduction to Business Finance',3),(20,'Statistics & Inferences',3),
    (21,'Human Resource Management',3),(22,'Enterprise Resource Planning',3),
    (23,'Organizational Behavior',3),(24,'Financial Management',3);
    INSERT dbo.CourseTbl (Course_Name,Credit_Hrs,DeptId,Department,Fac_Id,Fac_Name)
    SELECT c.Name + ' [Sample]',c.Credits,f.DeptId,f.Department,f.Id,f.Name
    FROM @courses c JOIN @faculty f
      ON f.Seq = CASE WHEN c.Seq <= 12 THEN 1 + (c.Seq - 1) % 6 ELSE 7 + (c.Seq - 13) % 6 END;

    INSERT dbo.FeesTbl (StId,StName,StDept,Amount,Semester,PayDate)
    SELECT s.Id,s.Name,s.Department,s.Fee / 2,s.Semester,p.PayDate
    FROM @students s CROSS JOIN (VALUES (CONVERT(date,'20260815')),(CONVERT(date,'20260915'))) p(PayDate);
    INSERT dbo.SalaryTbl (F_Id,F_Name,Department,Salary,PayDate)
    SELECT f.Id,f.Name,f.Department,f.Salary,p.PayDate
    FROM @faculty f CROSS JOIN (VALUES (CONVERT(date,'20260731')),(CONVERT(date,'20260831')),(CONVERT(date,'20260930'))) p(PayDate);

    DECLARE @after TABLE (Entity varchar(20), Records int, ExpectedAdded int);
    INSERT @after VALUES
    ('Departments',(SELECT COUNT(*) FROM dbo.DepartmentTbl),2),
    ('Campuses',(SELECT COUNT(*) FROM dbo.CampusTbl),3),
    ('Faculty',(SELECT COUNT(*) FROM dbo.FacultyTbl),12),
    ('Students',(SELECT COUNT(*) FROM dbo.StudentTbl),40),
    ('Courses',(SELECT COUNT(*) FROM dbo.CourseTbl),24),
    ('Fees',(SELECT COUNT(*) FROM dbo.FeesTbl),80),
    ('Salaries',(SELECT COUNT(*) FROM dbo.SalaryTbl),36);
    IF EXISTS (SELECT 1 FROM @after a JOIN @before b ON a.Entity = b.Entity WHERE a.Records - b.Records <> a.ExpectedAdded)
        THROW 51000,'Sample record count validation failed.',1;
    IF EXISTS (SELECT 1 FROM dbo.CourseTbl c JOIN @faculty f ON c.Fac_Id = f.Id
               WHERE c.DeptId <> f.DeptId OR c.Fac_Name <> f.Name OR c.Department <> f.Department)
        THROW 51001,'Sample course relationship validation failed.',1;
    IF EXISTS (SELECT 1 FROM @students s JOIN dbo.FeesTbl p ON p.StId = s.Id
               GROUP BY s.Id,s.Fee HAVING COUNT(*) <> 2 OR SUM(p.Amount) <> s.Fee)
        THROW 51002,'Sample fee validation failed.',1;
    IF EXISTS (SELECT 1 FROM @faculty f JOIN dbo.SalaryTbl p ON p.F_Id = f.Id
               GROUP BY f.Id,f.Salary HAVING COUNT(*) <> 3 OR SUM(p.Salary) <> f.Salary * 3)
        THROW 51003,'Sample salary validation failed.',1;
    INSERT dbo.UmsSchemaMetadata ([Key],[Value]) VALUES
    (N'KIETSampleData.v1',N'197 fictional sample records: 2 academic groups, 3 campuses, 12 faculty, 40 students, 24 courses, 80 fees, 36 salaries. KIET Karachi public academic context; names, dates, intake and amounts are illustrative.');
    COMMIT;
    SELECT a.Entity,b.Records AS PreviousRecords,a.ExpectedAdded AS AddedRecords,a.Records AS TotalRecords
    FROM @after a JOIN @before b ON a.Entity = b.Entity ORDER BY a.Entity;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK;
    THROW;
END CATCH;
