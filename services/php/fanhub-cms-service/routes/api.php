<?php

use Illuminate\Http\Request;
use Illuminate\Support\Facades\Route;

/*
|--------------------------------------------------------------------------
| API Routes
|--------------------------------------------------------------------------
|
| Here is where you can register API routes for your application. These
| routes are loaded by the RouteServiceProvider and all of them will
| be assigned to the "api" middleware group. Make something great!
|
*/

use App\Http\Controllers\Api\Admin\CategoryController;
use App\Http\Controllers\Api\Client\CommentController;
use App\Http\Controllers\Api\Client\GroupController;
use App\Http\Controllers\Api\Client\PostController;

Route::prefix('v1/admin')->middleware(['admin.jwt'])->group(function () {
    Route::get('categories', [CategoryController::class, 'index']);
    Route::get('categories/{id}', [CategoryController::class, 'show']);
    Route::post('categories', [CategoryController::class, 'store']);
    Route::put('categories/{id}', [CategoryController::class, 'update']);
    Route::delete('categories/{id}', [CategoryController::class, 'destroy']);
});

Route::prefix('v1')->middleware(['client.jwt'])->group(function () {
    // Posts & Likes
    Route::get('posts', [PostController::class, 'index']);
    Route::get('posts/{id}', [PostController::class, 'show']);
    Route::post('posts', [PostController::class, 'store']);
    Route::put('posts/{id}', [PostController::class, 'update']);
    Route::delete('posts/{id}', [PostController::class, 'destroy']);
    Route::post('posts/{id}/like', [PostController::class, 'toggleLike']);

    // Comments
    Route::post('posts/{id}/comments', [CommentController::class, 'store']);
    Route::get('posts/{id}/comments', [CommentController::class, 'index']);

    // Groups & Join
    Route::get('groups', [GroupController::class, 'index']);
    Route::get('groups/{id}', [GroupController::class, 'show']);
    Route::post('groups', [GroupController::class, 'store']);
    Route::post('groups/{id}/join', [GroupController::class, 'join']);
});


