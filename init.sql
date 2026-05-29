-- ============================================================
--  QUINIELA APP — Setup inicial SQL Server
-- ============================================================
USE master;
GO
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name='QuinielaAppDb')
    CREATE DATABASE QuinielaAppDb;
GO
USE QuinielaAppDb;
GO

-- Crear usuario admin (password: Admin123!)
-- El hash BCrypt ya está pre-generado para "Admin123!"
-- Puedes cambiarlo desde la app luego
INSERT INTO Users (FullName, Username, Email, PasswordHash, Role, CreatedAt)
SELECT 'Administrador', 'admin', 'admin@quiniela.com',
       '$2a$11$xzRkfwULjSNpQ4T5Y.K2aehpQ4BYMqNZ6rkU8ZpWcCW3.1oDtm5Ky',
       'Admin', GETUTCDATE()
WHERE NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'admin');
GO

PRINT 'Setup inicial completado.';
PRINT 'Usuario admin: admin@quiniela.com / Admin123!';
GO
