<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

class ReactionBookmark extends Model
{
    use HasFactory;

    protected $table = 'reaction_bookmarks';

    public $timestamps = false;

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'user_id',
        'target_type',
        'target_id',
        'action_type',
        'created_at',
    ];

    protected static function booted(): void
    {
        static::creating(function (ReactionBookmark $reaction) {
            if (empty($reaction->id)) {
                $reaction->id = 'req_' . Str::lower(Str::random(12));
            }
            if (empty($reaction->created_at)) {
                $reaction->created_at = now();
            }
        });
    }
}
