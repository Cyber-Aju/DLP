<?php
// Database connection
$host = 'localhost';
$db = 'aerologue_dlp';
$user = 'root'; // Change as needed
$pass = '1234';     // Change as needed

try {
    $pdo = new PDO("mysql:host=$host;dbname=$db;charset=utf8mb4", $user, $pass);
    $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
} catch (\PDOException $e) {
    die(json_encode(['status' => 'error', 'message' => 'DB Connection Failed' . $e]));
}
