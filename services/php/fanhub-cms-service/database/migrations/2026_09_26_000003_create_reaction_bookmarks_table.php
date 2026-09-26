<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;

return new class extends Migration
{
    /**
     * Run the migrations.
     */
    public function up(): void
    {
        Schema::create('reaction_bookmarks', function (Blueprint $table) {
            $table->string('id', 64)->primary();
            $table->string('user_id', 64)->index();
            $table->string('target_type', 50)->index();
            $table->string('target_id', 64)->index();
            $table->string('action_type', 50)->index(); // 'like', 'bookmark', etc.
            $table->timestamp('created_at')->useCurrent();

            $table->unique(['user_id', 'target_type', 'target_id', 'action_type'], 'user_target_action_unique');
        });
    }

    /**
     * Reverse the migrations.
     */
    public function down(): void
    {
        Schema::dropIfExists('reaction_bookmarks');
    }
};
