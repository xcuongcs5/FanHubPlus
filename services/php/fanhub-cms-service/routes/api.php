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
use App\Http\Controllers\Api\Admin\CharacterController;
use App\Http\Controllers\Api\Admin\CommentController as AdminCommentController;
use App\Http\Controllers\Api\Admin\ContentController;
use App\Http\Controllers\Api\Admin\DashboardController;
use App\Http\Controllers\Api\Admin\EventController;
use App\Http\Controllers\Api\Admin\FinancialController;
use App\Http\Controllers\Api\Admin\TagController;
use App\Http\Controllers\Api\Admin\UserController;
use App\Http\Controllers\Api\Client\CommentController;
use App\Http\Controllers\Api\Client\GroupController;
use App\Http\Controllers\Api\Client\PostController;

Route::prefix('v1/admin')->middleware(['admin.jwt'])->group(function () {
    // Characters Management
    Route::get('characters', [CharacterController::class, 'index']);
    Route::post('characters', [CharacterController::class, 'store']);
    Route::put('characters/{id}', [CharacterController::class, 'update']);
    Route::delete('characters/{id}', [CharacterController::class, 'destroy']);

    // Tags Management
    Route::get('tags', [TagController::class, 'index']);
    Route::post('tags', [TagController::class, 'store']);
    Route::delete('tags/{id}', [TagController::class, 'destroy']);

    // Comments Moderation
    Route::get('comments/flagged', [AdminCommentController::class, 'flagged']);
    Route::delete('comments/{id}', [AdminCommentController::class, 'destroy']);

    // Categories
    Route::get('categories', [CategoryController::class, 'index']);
    Route::get('categories/{id}', [CategoryController::class, 'show']);
    Route::post('categories', [CategoryController::class, 'store']);
    Route::put('categories/{id}', [CategoryController::class, 'update']);
    Route::delete('categories/{id}', [CategoryController::class, 'destroy']);

    // Contents Management
    Route::get('contents', [ContentController::class, 'index']);
    Route::post('contents', [ContentController::class, 'store']);
    Route::get('contents/{id}', [ContentController::class, 'show']);
    Route::put('contents/{id}/review', [ContentController::class, 'review']);
    Route::put('contents/{id}', [ContentController::class, 'update']);
    Route::delete('contents/{id}', [ContentController::class, 'destroy']);

    // Dashboard Overview & Stats
    Route::get('dashboard/overview', [DashboardController::class, 'overview']);
    Route::get('dashboard/stats/users', [DashboardController::class, 'statsUsers']);
    Route::get('dashboard/stats/revenue', [DashboardController::class, 'statsRevenue']);
    Route::get('dashboard/stats/categories', [DashboardController::class, 'statsCategories']);

    // Users Management
    Route::get('users', [UserController::class, 'index']);
    Route::post('users', [UserController::class, 'store']);
    Route::get('users/{id}', [UserController::class, 'show']);
    Route::put('users/{id}/status', [UserController::class, 'updateStatus']);
    Route::put('users/{id}/roles', [UserController::class, 'updateRoles']);
    Route::delete('users/{id}', [UserController::class, 'destroy']);
    Route::match(['put', 'post'], 'users/{id}/ban', [UserController::class, 'ban']);

    // Events Pending & Approve
    Route::get('events/pending', [EventController::class, 'pending']);
    Route::post('events/{id}/approve', [EventController::class, 'approve']);

    // Financial Reports
    Route::get('financial/reports', [FinancialController::class, 'reports']);
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


